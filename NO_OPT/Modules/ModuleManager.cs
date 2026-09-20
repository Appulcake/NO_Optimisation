using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NO_OPT.Modules;

internal sealed class ModuleManager : IDisposable
{
    private readonly Plugin _host;
    private readonly List<OptimisationModule> _modules = [];
    private readonly ModuleScope _runtimeScope;
    private bool _clientMasterEnabled = true;
    
    internal ModuleManager(Plugin host, bool headless)
    {
        _host = host;
        _runtimeScope = headless ? ModuleScope.Headless : ModuleScope.Client;
    }
    
    public void Dispose()
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
            _modules[i].StopCore();
        _modules.Clear();
    }
    
    internal void Initialise()
    {
        var moduleBase = typeof(OptimisationModule);
        var definitions =
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => !type.IsAbstract && moduleBase.IsAssignableFrom(type))
                .Select(type => new
                {
                    Type = type,
                    Attribute = type.GetCustomAttribute<OptimisationModuleAttribute>()
                })
                .Where(item => item.Attribute != null && (item.Attribute.Scope & _runtimeScope) != 0)
                .OrderBy(item => item.Attribute!.Order)
                .ThenBy(item => item.Type.FullName, StringComparer.Ordinal)
                .ToArray();
        
        foreach (var definition in definitions)
        {
            OptimisationModule? module = null;
            try
            {
                module = (OptimisationModule)Activator.CreateInstance(definition.Type, true)!;
                module.ConfigureCore(_host, definition.Attribute!);
                if (definition.Attribute!.Scope == ModuleScope.Client && definition.Attribute.RespectClientMaster)
                    module.SetManagerEnabledCore(_clientMasterEnabled);
                _modules.Add(module);
            }
            catch (Exception ex)
            {
                module?.StopCore();
                Plugin.LogError($"Failed configuring module {definition.Type.FullName}:\n{ex}");
            }
        }
        
        foreach (var module in _modules)
            module.StartCore();
    }
    
    internal void SetClientMasterEnabled(bool enabled)
    {
        _clientMasterEnabled = enabled;
        foreach (var module in _modules)
        {
            if (module.Metadata.Scope != ModuleScope.Client || !module.Metadata.RespectClientMaster)
                continue;
            
            module.SetManagerEnabledCore(enabled);
        }
    }
}