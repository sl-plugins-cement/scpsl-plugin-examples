using System;
using LabApi.Features;
using LabApi.Loader.Features.Plugins;
using Logger = LabApi.Features.Console.Logger;

namespace ToyTricksDemo;

/// <summary>
/// A reference gallery for the metarepo's in-game-verified AdminToy tricks — replicated shear (SVD
/// parent/child rig), the HDR bloom glow recipe, meter-calibrated world TextToys, a moving WaypointToy
/// carrying an FPC dummy + real pickup through native relative positioning, a button-triggered
/// firefight between animated RA dummies on two moving carriers, and a patrol of dummies that walk
/// through the native FPC motor while firing a shotgun — so future work can copy calibrated, known-good
/// code instead of rediscovering the traps. Spawned on demand with the RA command "toytricks"; the
/// plugin does nothing otherwise (apart from the opt-out dummy-firearm Harmony fixes, which repair two
/// native bugs that make non-automatic firearms unusable by dummies).
/// </summary>
public sealed class ToyTricksPlugin : Plugin<Config>
{
    public static ToyTricksPlugin? Instance { get; private set; }

    public override string Name => "ToyTricksDemo";

    public override string Description =>
        "Demo gallery for replicated shear, HDR glow, the strike array, calibrated world text, the waypoint carrier, the moving firefight, and the walking shotgun patrol (RA: toytricks).";

    public override string Author => "Cement";

    public override Version Version => new(1, 4, 3);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        Instance = this;
        ExhibitTools.TextUnitsToMeters = Config!.TextUnitsToMeters;
        if (!Config.IsEnabled)
        {
            Logger.Info("[ToyTricksDemo] Disabled by config (is_enabled: false).");
            return;
        }

        // Opt-out, because these are server-wide fixes to native firearm code rather than
        // plugin-local behavior. See Patches/DummyFirearmPatches.cs for exactly what they repair.
        if (Config.PatchDummyFirearms)
        {
            Patches.DummyFirearmPatches.Apply();
        }
    }

    public override void Disable()
    {
        DemoGallery.Clear();
        Patches.DummyFirearmPatches.Remove();
        Instance = null;
    }
}
