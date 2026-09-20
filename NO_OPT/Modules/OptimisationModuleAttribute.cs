using System;
using JetBrains.Annotations;

namespace NO_OPT.Modules;

[Flags]
internal enum ModuleScope : byte
{
    Client = 1,
    Headless = 2,
    Any = Client | Headless
}

[MeansImplicitUse]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class OptimisationModuleAttribute : Attribute
{
    // Always active module
    public OptimisationModuleAttribute(ModuleScope scope)
    {
        Scope = scope;
        Toggleable = false;
    }
    
    // Toggleable module
    public OptimisationModuleAttribute(ModuleScope scope, string section, string enableKey, bool defaultEnabled = true,
        string description = "")
    {
        Scope = scope;
        Section = section;
        EnableKey = enableKey;
        DefaultEnabled = defaultEnabled;
        Description = description;
        Toggleable = true;
    }
    
    internal ModuleScope Scope { get; }
    internal bool Toggleable { get; }
    internal string Section { get; } = string.Empty;
    internal string EnableKey { get; } = string.Empty;
    internal bool DefaultEnabled { get; }
    internal string Description { get; } = string.Empty;
    public bool LiveToggle { get; set; } = true;
    public int Order { get; set; }
    
    // Master switch for all client patches
    public bool RespectClientMaster { get; set; } = true;
}