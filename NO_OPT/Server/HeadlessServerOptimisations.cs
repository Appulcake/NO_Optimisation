using NO_OPT.Modules;
using UnityEngine;

namespace NO_OPT.Server;

[OptimisationModule(ModuleScope.Headless, "Headless Server", "0. Enable Headless Server Patches", LiveToggle = false)]
internal sealed partial class HeadlessServerOptimisations : OptimisationModule
{
    private bool _previousAudioPause;
    
    protected override void OnEnable()
    {
        _previousAudioPause = AudioListener.pause;
        AudioListener.pause = true;
    }
    
    protected override void OnDisable()
    {
        AudioListener.pause = _previousAudioPause;
    }
}