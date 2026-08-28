using System;
using HarmonyLib;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Firearms.Modules.Misc;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

namespace ToyTricksDemo.Patches;

/// <summary>
/// Harmony fixes for two NATIVE bugs that make non-automatic firearms unusable by RA dummies. Both are
/// strict repairs of a code path that, for a dummy, currently either throws or does nothing — neither
/// changes behavior for a real (local) player, whose branches never reach this code.
///
/// BACKGROUND. <c>AutosyncItem</c> defines
/// <c>IsControllable =&gt; IsLocalPlayer ? true : IsEmulatedDummy</c>, so a server-side dummy is
/// CONTROLLABLE but never LOCAL. Firearm action modules disagree about which of those two flags gates
/// the trigger, and only <see cref="AutomaticActionModule"/> gets the dummy case right:
///
/// <list type="bullet">
/// <item><see cref="AutomaticActionModule"/> — gates on IsControllable and routes the shot through
/// SendCmd, which for a dummy re-enters ServerProcessCmd carrying a real ShotBacktrackData. Works.</item>
/// <item><see cref="DoubleActionModule"/> (revolver) — gates on IsControllable and calls
/// <c>Fire(null)</c>, but <c>FireLive</c> then picks its backtrack source with <c>IsLocalPlayer</c> and
/// so reads from that null reader. NullReferenceException every frame, forever. Patch #1.</item>
/// <item><see cref="PumpActionModule"/> (shotgun) — gates its ONLY shot-enqueueing path on
/// IsLocalPlayer, so a dummy's trigger reaches nothing at all. Silent no-op. Patch #2.</item>
/// </list>
///
/// Scope note: these are global (every revolver/shotgun on the server), which is why they are opt-in
/// via the <c>patch_dummy_firearms</c> config key rather than always-on.
/// </summary>
internal static class DummyFirearmPatches
{
    private const string HarmonyId = "toytricksdemo.dummyfirearms";

    private static Harmony? _harmony;

    public static bool IsApplied => _harmony != null;

    public static void Apply()
    {
        if (_harmony != null)
        {
            return;
        }

        try
        {
            Harmony harmony = new(HarmonyId);
            harmony.PatchAll(typeof(DummyFirearmPatches).Assembly);
            _harmony = harmony;
            Logger.Info(
                "[ToyTricksDemo] Dummy-firearm Harmony fixes applied (revolver null-backtrack crash + " +
                "shotgun dummy trigger). Disable with patch_dummy_firearms: false.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[ToyTricksDemo] Failed to apply dummy-firearm patches: {exception}");
            _harmony = null;
        }
    }

    public static void Remove()
    {
        Harmony? harmony = _harmony;
        _harmony = null;
        if (harmony == null)
        {
            return;
        }

        try
        {
            harmony.UnpatchAll(HarmonyId);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo] Failed to remove dummy-firearm patches: {exception.GetBaseException().Message}");
        }
    }
}

/// <summary>
/// PATCH #1 — revolver crash fix.
///
/// <c>DoubleActionModule.EquipUpdate</c> calls <c>Fire(null)</c> for anything IsControllable, and
/// <c>FireLive</c> resolves its backtrack as
/// <c>(IsLocalPlayer ? new ShotBacktrackData(Firearm) : new ShotBacktrackData(extraData))</c>. A dummy
/// takes the second branch with <paramref name="extraData"/> = null, and
/// <c>ShotBacktrackData(NetworkReader)</c> immediately calls <c>reader.ReadRelativePosition()</c> →
/// NullReferenceException, once per frame for as long as the trigger is held.
///
/// The repair matches the native intent exactly: when there is no incoming reader, build the backtrack
/// from the firearm — the same public <c>ShotBacktrackData(Firearm)</c> constructor the local-player
/// branch uses, which reads the owner's live FPC position/camera rotation and resolves a primary target.
/// We hand it to the original method through a synthetic reader so the untouched native code runs.
/// </summary>
[HarmonyPatch(typeof(DoubleActionModule), "FireLive")]
internal static class DoubleActionModuleFireLivePatch
{
    /// <summary>
    /// The same <c>IsLocalPlayer</c>-vs-<c>IsControllable</c> mismatch appears a THIRD time, in
    /// <c>Fire</c>:
    /// <code>
    /// if (base.IsLocalPlayer) { _triggerPull.Reset(); ... }   // dummy never gets here
    /// else                    { /* cooldown only, no Reset */ }
    /// </code>
    /// so a dummy's <c>_triggerPull</c> stays <c>IsPulling</c> forever and <c>EquipUpdate</c> re-fires
    /// once per <c>TimeBetweenShots</c> indefinitely — one click becomes an endless stream. Observed
    /// in-game 2026-07-25 right after patch #1 made the revolver fire at all.
    ///
    /// <c>TriggerPull</c> is a private nested class, so this resets it through the module's own private
    /// <c>_triggerPull</c> field and that class's public <c>Reset()</c> — no state is invented, we just
    /// perform the reset the local-player branch would have done.
    /// </summary>
    private static readonly AccessTools.FieldRef<DoubleActionModule, object>? TriggerPullRef =
        TryGetTriggerPullRef();

    private static AccessTools.FieldRef<DoubleActionModule, object>? TryGetTriggerPullRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<DoubleActionModule, object>("_triggerPull");
        }
        catch (Exception exception)
        {
            Logger.Warn(
                "[ToyTricksDemo] Revolver single-shot fix inert: field '_triggerPull' not found on " +
                $"DoubleActionModule ({exception.GetBaseException().Message}). A dummy firing a revolver " +
                "will repeat-fire until it is holstered.");
            return null;
        }
    }

    /// <summary>
    /// Ends the dummy's trigger pull, so one press produces exactly one shot. Called from the
    /// <c>Fire</c> patch rather than here: <c>FireLive</c> only runs when the chamber holds a live
    /// round, so resetting here would leave a dry fire looping forever.
    /// </summary>
    internal static void ResetDummyTriggerPull(DoubleActionModule module)
    {
        if (TriggerPullRef == null || module.Firearm?.Owner == null || !module.Firearm.Owner.IsDummy)
        {
            return;
        }

        try
        {
            object triggerPull = TriggerPullRef(module);
            AccessTools.Method(triggerPull.GetType(), "Reset")?.Invoke(triggerPull, null);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo] Revolver trigger reset failed: {exception.GetBaseException().Message}");
        }
    }

    private static bool Prefix(DoubleActionModule __instance, ref NetworkReader extraData)
    {
        // Only the null case is ours; a real client's shot carries its own reader and is left alone.
        if (extraData != null)
        {
            return true;
        }

        Firearm? firearm = __instance.Firearm;
        if (firearm == null || firearm.Owner == null)
        {
            // Nothing sane to build from — skip the original rather than let it throw.
            return false;
        }

        try
        {
            // Serialize a firearm-derived backtrack and hand the original a reader over those bytes,
            // so ProcessShot runs on real data through the native code path.
            //
            // The bytes are COPIED out of the pooled writer: a NetworkReader is a view over its buffer,
            // and returning the writer to the pool would let another caller overwrite the array while
            // the original method is still reading it.
            byte[] payload;
            NetworkWriterPooled writer = NetworkWriterPool.Get();
            try
            {
                new ShotBacktrackData(firearm).WriteSelf(writer);
                payload = writer.ToArray();
            }
            finally
            {
                NetworkWriterPool.Return(writer);
            }

            extraData = new NetworkReader(new ArraySegment<byte>(payload));
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"[ToyTricksDemo] Revolver backtrack synthesis failed for {firearm.Owner.nicknameSync?.MyNick}: " +
                $"{exception.GetBaseException().Message}. Suppressing the shot instead of crashing.");
            return false;
        }
    }
}

/// <summary>
/// PATCH #1b — revolver single-shot.
///
/// Companion to the crash fix: <c>DoubleActionModule.Fire</c> resets the trigger pull only on the
/// <c>IsLocalPlayer</c> branch, so a dummy's <c>_triggerPull</c> stays <c>IsPulling</c> and
/// <c>EquipUpdate</c> re-fires every <c>TimeBetweenShots</c> forever. Patched on <c>Fire</c> rather than
/// <c>FireLive</c> because <c>FireLive</c> is skipped for a dry chamber, which would loop just as badly.
/// </summary>
[HarmonyPatch(typeof(DoubleActionModule), "Fire")]
internal static class DoubleActionModuleFirePatch
{
    private static void Postfix(DoubleActionModule __instance)
        => DoubleActionModuleFireLivePatch.ResetDummyTriggerPull(__instance);
}

/// <summary>
/// PATCH #2 — shotgun dummy trigger.
///
/// <c>PumpActionModule.EquipUpdate</c> reads:
/// <code>
/// if (base.IsLocalPlayer) { UpdateClient(); }   // &lt;-- not IsControllable
/// if (base.IsServer)      { UpdateServer(); }
/// </code>
/// <c>UpdateClient</c> is the only method that ever enqueues a shot, and the whole file contains zero
/// references to IsControllable/IsEmulatedDummy. So a dummy holding a shotgun has a visible
/// "Shoot-&gt;Click" row (SimpleTriggerModule registers the listener regardless) that does nothing at all
/// — no shot, no damage, no exception.
///
/// The repair drives the same enqueue the client path performs, for dummies only: when the native
/// trigger is held and the weapon is ready, push one firearm-derived ShotBacktrackData into the module's
/// server queue, which <c>UpdateServer</c> then drains through the ordinary
/// <c>ProcessShot → hitreg</c> flow. Rate limiting is enforced by the module's own PumpIdle/AnyModuleBusy
/// checks in UpdateServer plus a local cooldown mirroring <c>CooldownAfterShot</c>.
///
/// This one necessarily touches a private queue (there is no public enqueue), so it is deliberately
/// conservative: dummies only, one shot per trigger transition, and it never fabricates chamber state —
/// <c>ServerProcessShot</c> still decides whether a round actually exists via <c>PullTrigger</c>.
/// </summary>
[HarmonyPatch(typeof(PumpActionModule), "EquipUpdate")]
internal static class PumpActionModuleEquipUpdatePatch
{
    /// <summary>
    /// Floor between queued dummy shots. Deliberately generous: the module's native PumpIdle /
    /// AnyModuleBusy gating in UpdateServer is the authoritative rate limit, and it still runs.
    /// </summary>
    private const float MinSecondsBetweenDummyShots = 0.75f;

    private static readonly AccessTools.FieldRef<PumpActionModule, object>? QueueRef =
        TryGetFieldRef("_serverQueuedShots");

    private static AccessTools.FieldRef<PumpActionModule, object>? TryGetFieldRef(string name)
    {
        try
        {
            return AccessTools.FieldRefAccess<PumpActionModule, object>(name);
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"[ToyTricksDemo] Shotgun dummy-fire patch inert: field '{name}' not found on PumpActionModule " +
                $"({exception.GetBaseException().Message}). A game update probably renamed it.");
            return null;
        }
    }

    private static void Postfix(PumpActionModule __instance)
    {
        if (QueueRef == null)
        {
            return;
        }

        Firearm? firearm = __instance.Firearm;
        if (firearm?.Owner == null || !firearm.Owner.IsDummy || !NetworkServer.active)
        {
            return;
        }

        try
        {
            // Native readiness gates only — never fabricate chamber/pump state.
            if (!__instance.Firearm.TryGetModule(out ITriggerControllerModule trigger) || !trigger.TriggerHeld)
            {
                DummyShotGate.Release(__instance);
                return;
            }

            // One shot per trigger press. CooldownAfterShot is private and attachment-scaled; rather
            // than reflect into it, this gate uses a conservative fixed floor — the module's own
            // PumpIdle / AnyModuleBusy checks in UpdateServer are the real rate limiter, and they run
            // untouched. This only stops a held trigger from flooding the queue between those checks.
            if (!DummyShotGate.TryConsume(__instance, MinSecondsBetweenDummyShots))
            {
                return;
            }

            object queue = QueueRef(__instance);
            AccessTools.Method(queue.GetType(), "Enqueue")
                ?.Invoke(queue, new object[] { new ShotBacktrackData(firearm) });
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"[ToyTricksDemo] Shotgun dummy-fire enqueue failed: {exception.GetBaseException().Message}");
        }
    }
}

/// <summary>
/// Per-module edge detection + cooldown for the shotgun dummy patch, so a held native trigger produces
/// one queued shot per <c>CooldownAfterShot</c> rather than one per frame.
/// </summary>
internal static class DummyShotGate
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Box> State = new();

    private sealed class Box
    {
        public bool Held;
        public float NextAllowedAt;
    }

    public static bool TryConsume(object module, float cooldownSeconds)
    {
        Box box = State.GetOrCreateValue(module);
        float now = UnityEngine.Time.timeSinceLevelLoad;
        box.Held = true;
        if (now < box.NextAllowedAt)
        {
            return false;
        }

        box.NextAllowedAt = now + UnityEngine.Mathf.Max(0.1f, cooldownSeconds);
        return true;
    }

    public static void Release(object module) => State.GetOrCreateValue(module).Held = false;
}
