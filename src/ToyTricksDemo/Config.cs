using System.ComponentModel;

namespace ToyTricksDemo;

public sealed class Config
{
    [Description("Master switch: when false the RA command refuses to run and no Harmony patches are applied.")]
    public bool IsEnabled { get; set; } = true;

    [Description("How many meters in front of the admin the gallery spawns.")]
    public float SpawnDistanceMeters { get; set; } = 5.5f;

    [Description("TextToy calibration factor: rect width in meters = DisplaySize.x * scale * this. " +
                 "0.05 was measured in-game 2026-07-19 (ruler edge markers exactly on the 1 m bar ends).")]
    public float TextUnitsToMeters { get; set; } = 0.05f;

    [Description("Apply Harmony fixes for two native bugs that stop RA dummies using non-automatic " +
                 "firearms: the revolver's per-frame NullReferenceException (DoubleActionModule passes a " +
                 "null backtrack reader for any non-local controllable player) and the shotgun's dead " +
                 "trigger (PumpActionModule gates its only shot-enqueue path on IsLocalPlayer). These are " +
                 "SERVER-WIDE and affect every revolver/shotgun, not just this plugin's dummies — real " +
                 "players are unaffected because their code paths never reach the patched branches. " +
                 "Set false to run the gallery without any Harmony patching.")]
    public bool PatchDummyFirearms { get; set; } = true;

    [Description("Weapon the firefight bay's shooter uses. GunAK works unpatched; GunRevolver and " +
                 "GunShotgun require patch_dummy_firearms: true (see above) and will otherwise crash or " +
                 "silently fail to fire.")]
    public string FirefightWeapon { get; set; } = "GunRevolver";
}
