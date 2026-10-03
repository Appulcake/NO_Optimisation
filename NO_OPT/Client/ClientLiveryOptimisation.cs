using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using NO_OPT.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace NO_OPT.Client;

[OptimisationModule(ModuleScope.Client, "--- Client ---", "5. Livery Load Stutter", true,
    "Checks workshop liveries before Unity loads them and cancels loading RGB24 textures that can cause " +
    "severe stutters (from running RemapFastARGB32_RGBA32) every time someone spawns with one.")]
internal sealed class ClientLiveryOptimisation : OptimisationModule
{
    private static readonly ConcurrentDictionary<ulong, PreflightResult> Cache = new();
    private static readonly ConcurrentDictionary<ulong, Lazy<Task<PreflightResult>>> InFlight = new();
    
    protected override void OnDisable()
    {
        Cache.Clear();
        InFlight.Clear();
    }
    
    private static async UniTask LoadWorkshopPreflighted(LiveryBehaviour behaviour, LiveryKey key, LiveryKey? fallback,
        CancellationToken cancel)
    {
        if (cancel == CancellationToken.None)
            cancel = behaviour.destroyCancellationToken;
        
        var aircraft = behaviour.aircraft;
        behaviour.key = key;
        behaviour.hasLoaded = true;
        var success = false;
        AsyncOperationHandle<LiveryData> loadedHandle = default;
        try
        {
            behaviour.IsLoading = true;
            if (!key.CanLoad(aircraft, out var folder) || string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                Plugin.Log($"Workshop livery source unavailable for {key} in folder \"{folder ?? "(null)"}\"");
            }
            else
            {
                Plugin.Log($"Workshop livery source {key} from folder \"{folder}\" resolved!");
                PreflightResult preflight;
                if (Cache.TryGetValue(key.Id, out var cached))
                {
                    preflight = cached;
                    Plugin.Log($"Preflight cache hit for {key}, with result: {preflight.Decision}");
                }
                else
                {
                    Plugin.Log($"Starting preflight on {key}...");
                    preflight = await GetOrStartPreflight(key.Id, folder);
                    await UniTask.SwitchToMainThread();
                    Plugin.Log($"Preflight for {key} finished with result: {preflight.Decision}");
                }
                
                if (cancel.IsCancellationRequested)
                {
                    Plugin.Log($"Workshop load cancelled after preflight for {key}");
                    return;
                }
                
                if (preflight.Decision == PreflightDecision.Safe)
                {
                    Plugin.Log($"Workshop entry {key} approved to load, entering vanilla LoadImpl");
                    loadedHandle = await key.LoadImpl(aircraft, folder);
                    success = loadedHandle.IsValid();
                    Plugin.Log($"Workshop LoadImpl finished for {key} successfully: {success}");
                    if (cancel.IsCancellationRequested)
                    {
                        ReleaseHandle(ref loadedHandle);
                        return;
                    }
                    
                    if (success)
                    {
                        behaviour.SetLivery(loadedHandle.Result);
                        var previousHandle = behaviour.handle;
                        behaviour.handle = loadedHandle;
                        loadedHandle = default;
                        ReleaseHandle(ref previousHandle);
                        Plugin.Log($"Workshop livery {key} applied");
                    }
                }
                else
                {
                    switch (preflight.Decision)
                    {
                        case PreflightDecision.Rgb24:
                            Plugin.Log($"Workshop livery load for {key} blocked, it's in RGB24 format " +
                                       $"(Texture \"{preflight.TextureName}\", {preflight.Width}x{preflight.Height})");
                            break;
                        
                        default:
                            Plugin.LogWarning($"Workshop livery load for {key} blocked, it's in unknown format: " +
                                              $"\"{preflight.Reason}\"");
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarning($"Exception loading Workshop livery {key}:\n{ex}");
        }
        finally
        {
            ReleaseHandle(ref loadedHandle);
            behaviour.IsLoading = false;
        }
        
        if (!success)
        {
            var locality = aircraft != null && aircraft.HasAuthority ? "local" : "remote";
            Plugin.Log($"Workshop livery {key} load failed/stopped for {locality} player");
            if (fallback.HasValue)
            {
                Plugin.Log($"Loading fallback for {key} => {fallback.Value}");
                await behaviour.Load(fallback.Value, null, cancel);
            }
        }
    }
    
    private static void ReleaseHandle(ref AsyncOperationHandle<LiveryData> handle)
    {
        if (!handle.IsValid())
            return;
        
        Addressables.Release(handle);
        handle = default;
    }
    
    private static async UniTask<PreflightResult> GetOrStartPreflight(ulong workshopId, string folder)
    {
        if (Cache.TryGetValue(workshopId, out var cached))
            return cached;
        
        // Make sure the same livery is not being scanned multiple times simultaneously
        var lazy = InFlight.GetOrAdd(workshopId, _ =>
            new Lazy<Task<PreflightResult>>(() =>
                Task.Run(() => ScanWorkshopFolder(folder)), LazyThreadSafetyMode.ExecutionAndPublication));
        
        try
        {
            var result = await lazy.Value;
            if (result.Decision != PreflightDecision.Failed)
                Cache[workshopId] = result;
            return result;
        }
        finally
        {
            InFlight.TryRemove(workshopId, out _);
        }
    }
    
    private static PreflightResult ScanWorkshopFolder(string folder)
    {
        try
        {
            var bundles = FindUnityBundles(folder);
            if (bundles.Count == 0)
                return PreflightResult.Failed("No Unity asset bundle was found in the livery folder.");
            
            var textureCount = 0;
            foreach (var bundlePath in bundles)
            {
                var bundleResult = ScanBundle(bundlePath);
                textureCount += bundleResult.TextureCount;
                if (bundleResult.Decision == PreflightDecision.Rgb24)
                    return bundleResult.ReturnResult(textureCount);
                
                if (bundleResult.Decision == PreflightDecision.Failed)
                    return PreflightResult.Failed(
                        $"Bundle \"{Path.GetFileName(bundlePath)}\" could not be safely inspected: {bundleResult.Reason}");
            }
            
            if (textureCount == 0)
                return PreflightResult.Failed("No Texture2D assets were found in the livery bundle.");
            
            return PreflightResult.Safe(textureCount);
        }
        catch (Exception ex)
        {
            return PreflightResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }
    
    private static PreflightResult ScanBundle(string bundlePath)
    {
        var manager = new AssetsManager();
        try
        {
            // AssetTools v3 allows inspecting uncompressed and LZ4 compressed bundles without fully loading and
            // decompressing them into memory, allowing having a peek at just a bit of metadata of the Texture2D asset
            var bundle = manager.LoadBundleFile(bundlePath, false);
            if (bundle == null || bundle.file == null)
                return PreflightResult.Failed("AssetsTools.NET returned a null bundle.");
            
            // LZMA unfortunately doesn't allow this quick peek without fully decompressing it, so skip these entirely :(
            if (bundle.originalCompression == AssetBundleCompressionType.LZMA)
                return PreflightResult.Failed("Bundle uses LZMA compression, skipping preflight.");
            
            var textureCount = 0;
            var fileNames = bundle.file.GetAllFileNames();
            for (var fileIndex = 0; fileIndex < fileNames.Count; fileIndex++)
            {
                if (!bundle.file.IsAssetsFile(fileIndex))
                    continue;
                
                var assets = manager.LoadAssetsFileFromBundle(bundle, fileIndex);
                if (assets == null || assets.file == null)
                    return PreflightResult.Failed($"Could not open serialised assets file \"{fileNames[fileIndex]}\".");
                
                if (!assets.file.Metadata.TypeTreeEnabled)
                    return PreflightResult.Failed($"Assets file \"{fileNames[fileIndex]}\" has no type tree.");
                
                foreach (var textureInfo in assets.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    textureCount++;
                    if (!TryReadTextureMetadata(manager, assets, textureInfo, out var texture, out var failure))
                        return PreflightResult.Failed($"Texture2D metadata could not be read: {failure}", textureCount);
                    
                    // To-do: find potentially safe versions (small enough resolution/mipcount?) which still allow
                    // loading RGB24 texture formats without a (massive) frametime stutter
                    // For now, block any and all RGB24
                    if (texture.Format == (int)TextureFormat.RGB24)
                        return PreflightResult.Rgb24(texture.Name, texture.Width, texture.Height, texture.MipCount,
                            textureCount);
                }
            }
            
            return PreflightResult.Safe(textureCount);
        }
        catch (Exception ex)
        {
            return PreflightResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            manager.UnloadAll();
        }
    }
    
    private static bool TryReadTextureMetadata(AssetsManager manager, AssetsFileInstance assets,
        AssetFileInfo textureInfo, out TextureMetadata texture, out string failure)
    {
        texture = default;
        failure = string.Empty;
        try
        {
            var template = manager.GetTemplateBaseField(assets, textureInfo);
            if (template == null)
            {
                failure = "Texture2D template field was null.";
                return false;
            }
            
            var reader = assets.file.Reader;
            var name = string.Empty;
            var width = 0;
            var height = 0;
            var mipCount = 0;
            lock (assets.LockReader)
            {
                var iterator = new AssetTypeValueIterator(template, reader,
                    textureInfo.GetAbsoluteByteOffset(assets.file), manager.GetRefTypeManager(assets));
                while (iterator.ReadNext())
                {
                    var fieldName = iterator.TempField.Name;
                    switch (fieldName)
                    {
                        case "m_Name":
                            name = iterator.ReadValueField().AsString;
                            break;
                        
                        case "m_Width":
                            width = iterator.ReadValueField().AsInt;
                            break;
                        
                        case "m_Height":
                            height = iterator.ReadValueField().AsInt;
                            break;
                        
                        case "m_MipCount":
                            mipCount = iterator.ReadValueField().AsInt;
                            break;
                        
                        case "m_TextureFormat":
                            var format = iterator.ReadValueField().AsInt;
                            texture = new TextureMetadata(name, width, height, mipCount, format);
                            return true;
                    }
                }
            }
            
            failure = "Texture2D had no m_TextureFormat field.";
            return false;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
    
    private static List<string> FindUnityBundles(string root)
    {
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            if (LooksLikeUnityBundle(path))
                result.Add(path);
        return result;
    }
    
    private static bool LooksLikeUnityBundle(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length < 7)
                return false;
            
            var header = new byte[8];
            var read = stream.Read(header, 0, header.Length);
            if (read < 7)
                return false;
            
            var signature = Encoding.ASCII.GetString(header, 0, read);
            return signature.StartsWith("UnityFS", StringComparison.Ordinal) ||
                   signature.StartsWith("UnityRaw", StringComparison.Ordinal) ||
                   signature.StartsWith("UnityWeb", StringComparison.Ordinal);
        }
        catch
        {
            // Handle steam still locking/moving the file if it's downloaded dynamically
            return false;
        }
    }
    
    [HarmonyPatch]
    private static class LiveryPatches
    {
        [HarmonyTargetMethod]
        [UsedImplicitly]
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LiveryBehaviour), nameof(LiveryBehaviour.Load),
                [typeof(LiveryKey), typeof(LiveryKey?), typeof(CancellationToken)]);
        
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool LoadPrefix(LiveryBehaviour __instance, LiveryKey key, LiveryKey? fallback,
            // ReSharper disable once InconsistentNaming
            CancellationToken cancel, ref UniTask __result)
        {
#pragma warning disable Harmony003
            if (key.Type != LiveryKey.KeyType.Workshop)
#pragma warning restore Harmony003
            {
                Plugin.Log($"Passing through non-workshop livery {key}");
                return true;
            }
            
            Plugin.Log($"Intercepting workshop livery {key} load");
            __result = LoadWorkshopPreflighted(__instance, key, fallback, cancel);
            return false;
        }
    }
    
    private enum PreflightDecision : byte
    {
        Safe,
        Rgb24,
        Failed
    }
    
    private readonly struct TextureMetadata
    {
        internal TextureMetadata(string name, int width, int height, int mipCount, int format)
        {
            Name = name;
            Width = width;
            Height = height;
            MipCount = mipCount;
            Format = format;
        }
        
        internal string Name { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int MipCount { get; }
        internal int Format { get; }
    }
    
    private readonly struct PreflightResult
    {
        private PreflightResult(PreflightDecision decision, int textureCount, string textureName, int width, int height,
            int mipCount, string reason)
        {
            Decision = decision;
            TextureCount = textureCount;
            TextureName = textureName;
            Width = width;
            Height = height;
            MipCount = mipCount;
            Reason = reason;
        }
        
        internal PreflightDecision Decision { get; }
        internal int TextureCount { get; }
        internal string TextureName { get; }
        internal int Width { get; }
        internal int Height { get; }
        private int MipCount { get; }
        internal string Reason { get; }
        
        internal static PreflightResult Safe(int textureCount) =>
            new(PreflightDecision.Safe, textureCount, string.Empty, 0, 0, 0, string.Empty);
        
        internal static PreflightResult
            Rgb24(string textureName, int width, int height, int mipCount, int textureCount) =>
            new(PreflightDecision.Rgb24, textureCount, textureName, width, height, mipCount, string.Empty);
        
        internal static PreflightResult Failed(string reason) =>
            new(PreflightDecision.Failed, 0, string.Empty, 0, 0, 0, reason);
        
        internal static PreflightResult Failed(string reason, int textureCount) => new(PreflightDecision.Failed,
            textureCount, string.Empty, 0, 0, 0, reason);
        
        internal PreflightResult ReturnResult(int textureCount) =>
            new(Decision, textureCount, TextureName, Width, Height, MipCount, Reason);
    }
}