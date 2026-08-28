using System;
using System.Collections.Generic;
using System.Linq;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using RelativePositioning;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using Player = LabApi.Features.Wrappers.Player;

namespace ToyTricksDemo;

/// <summary>
/// One animated RA dummy for the demo gallery: spawn, role, placement, native aim, and the native
/// trigger pull. This is a DEMO-scale distillation of the batteries-included verbs in
/// <c>.tests\Playtest\Actors\{Actor,CombatFulfiller,MovementFulfiller}.cs</c> — same native paths, no
/// telemetry/fidelity/monitor machinery (the harness owns those; a gallery exhibit must not fail a run):
///
/// - Spawn: <see cref="DummyUtils.SpawnDummy"/> — the exact RA "dummy" path.
/// - Deferred role: the role is set ONE FRAME after spawn. A same-frame ServerSetRole races
///   PlayerAuthenticationManager.Start's UserId assignment and NREs inside keycard loadout code
///   (SerialNumberDetail.GetNumberForPlayer) — harness note, reproduced verbatim.
/// - Aim: <see cref="FpcMouseLook"/> CurrentHorizontal (body yaw 0..360) / CurrentVertical (camera
///   pitch, +up, clamped ±88). The server applies both to hub.transform + PlayerCameraReference in
///   FpcMouseLook.UpdateRotation, which for a dummy runs with lerp t=1 (instant, no smoothing).
/// - Fire: the "Shoot-&gt;Click" dummy action from DummyActionCollector.ServerGetActions — byte-identical
///   to an admin clicking that row in the RA dummy panel, so the whole server firearm pipeline runs
///   (backtrack, hitreg, PlayerShootingWeapon events, hitmarkers, real damage).
///
/// Nothing here fabricates firearm state: readiness comes from the native "Reload-&gt;Click" action.
/// </summary>
internal sealed class DemoActor
{
    /// <summary>Reserve ammo handed to a firearm actor. Cleared on teardown so no pickups spawn.</summary>
    private const int ReserveAmmo = 60;

    private DemoActor(ReferenceHub hub, string label)
    {
        Hub = hub;
        Label = label;
    }

    public ReferenceHub Hub { get; }

    public string Label { get; }

    public bool IsAlive => Hub != null
        && Hub.gameObject != null
        && Hub.roleManager?.CurrentRole is IFpcRole
        && Health > 0f;

    public float Health => Hub != null && Hub.gameObject != null
        ? Hub.playerStats.GetModule<PlayerStatsSystem.HealthStat>().CurValue
        : 0f;

    public RoleTypeId Role => Hub?.roleManager?.CurrentRole?.RoleTypeId ?? RoleTypeId.None;

    /// <summary>Live world position (the hub transform, i.e. the FPC capsule origin at the feet).</summary>
    public Vector3 Position => Hub != null && Hub.gameObject != null ? Hub.transform.position : Vector3.zero;

    /// <summary>
    /// Spawns the dummy far below the map so the client never sees it at the origin, then returns
    /// immediately — the caller must yield <see cref="InitializeRole"/> before placing it.
    /// </summary>
    public static DemoActor? TrySpawn(string label, Vector3 parkPosition)
    {
        try
        {
            ReferenceHub? hub = DummyUtils.SpawnDummy(label);
            if (hub == null || hub.gameObject == null)
            {
                Logger.Warn($"[ToyTricksDemo:Actor] SpawnDummy returned no hub for '{label}'.");
                return null;
            }

            hub.transform.position = parkPosition;
            return new DemoActor(hub, label);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] SpawnDummy('{label}') failed: {exception.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// Assigns the role one frame after spawn (see the UserId race in the class remarks) and waits for
    /// the FPC module to come up. Yields false-equivalent by leaving <see cref="IsAlive"/> false.
    /// </summary>
    public IEnumerator<float> InitializeRole(RoleTypeId role, bool withLoadout)
    {
        yield return Timing.WaitForOneFrame;
        if (Hub == null || Hub.gameObject == null)
        {
            yield break;
        }

        try
        {
            Hub.roleManager.ServerSetRole(
                role,
                RoleChangeReason.RemoteAdmin,
                withLoadout ? RoleSpawnFlags.AssignInventory : RoleSpawnFlags.None);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] Role assignment for {Label} failed: {exception.GetBaseException().Message}");
            yield break;
        }

        // The capsule/FPC module initializes over the following frames; placement math before that
        // lands on a role that has no FpcModule yet.
        float deadline = Time.timeSinceLevelLoad + 4f;
        while (Time.timeSinceLevelLoad < deadline
               && (Role != role || Hub.roleManager.CurrentRole is not IFpcRole))
        {
            yield return Timing.WaitForOneFrame;
        }

        if (Role != role)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] {Label} never became {role} (still {Role}).");
        }

        yield return Timing.WaitForSeconds(0.25f);
    }

    /// <summary>
    /// Teleports the dummy. <paramref name="position"/> is the FPC capsule CENTRE (~1 m above the
    /// feet), so a payload standing on a platform surface needs surfaceY + ~1.
    /// </summary>
    public void PlaceAt(Vector3 position, Quaternion rotation)
    {
        Player? player = Player.Get(Hub);
        if (player == null || player.IsDestroyed)
        {
            return;
        }

        player.Position = position;
        player.Rotation = rotation;
        LookAlong(rotation);
    }

    /// <summary>
    /// Walks one step toward a world target through the NATIVE motor.
    ///
    /// <see cref="FpcMotor.ReceivedPosition"/> is the field a real client's position updates land in,
    /// and the motor walks toward it with real speed/collision/step-up. THE CATCH (from
    /// <c>FpcMotor.DesiredMove</c>): the motor only walks when the remaining horizontal distance is
    /// below <c>_lastMaxSpeed</c> — i.e. reachable within roughly one tick. A target further away is
    /// treated as a desync and answered with <c>ServerOverridePosition(Position)</c>, which pins the
    /// dummy in place. A far-off destination therefore produces NO movement at all, which is exactly
    /// how the first patrol build failed.
    ///
    /// So the destination is fed in SMALL STEPS: each call advances the received position by at most
    /// <paramref name="stepMeters"/> along the direction of travel, keeping it inside the motor's
    /// accept window. That is also how a real client looks to the server — a stream of nearby positions,
    /// not one distant goal. The dummy's own capsule/collision still resolves the actual movement, so
    /// walls and steps behave normally.
    /// </summary>
    public bool WalkTowards(Vector3 worldTarget, float stepMeters = 0.45f)
    {
        if (Hub.roleManager.CurrentRole is not IFpcRole fpc)
        {
            return false;
        }

        Vector3 current = Hub.transform.position;
        Vector3 delta = worldTarget - current;
        delta.y = 0f;

        float distance = delta.magnitude;
        Vector3 step = distance <= stepMeters
            ? worldTarget
            : current + (delta / distance * stepMeters);

        // Keep the commanded height at the dummy's own feet: a step aimed above/below the floor makes
        // the motor treat it as a fall/climb rather than a walk.
        step.y = current.y;
        fpc.FpcModule.Motor.ReceivedPosition = new RelativePosition(step);
        return true;
    }

    /// <summary>Horizontal distance to a world point, ignoring height (walk targets are ground-plane).</summary>
    public float FlatDistanceTo(Vector3 worldPoint)
    {
        Vector3 delta = worldPoint - Hub.transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    /// <summary>
    /// True when the native movement module has selected <paramref name="waypoint"/> as its
    /// relative-positioning anchor — i.e. this dummy will ride that carrier. Selection happens on a
    /// LATER frame than placement, so poll this before moving the platform; until it flips the dummy is
    /// standing in world space and a moving deck slides out from under it.
    /// </summary>
    public bool IsOnWaypoint(AdminToyWaypoint waypoint)
    {
        if (Hub == null || Hub.gameObject == null || Hub.roleManager.CurrentRole is not IFpcRole fpc)
        {
            return false;
        }

        byte waypointId = fpc.FpcModule.RelativePosition.WaypointId;
        return waypointId != 0
            && WaypointBase.TryGetWaypoint(waypointId, out AdminToyWaypoint selected)
            && selected == waypoint;
    }

    /// <summary>Points the body yaw along a rotation (keeps aim and model facing consistent after a place).</summary>
    public void LookAlong(Quaternion rotation)
    {
        if (Hub.roleManager.CurrentRole is not IFpcRole fpc)
        {
            return;
        }

        fpc.FpcModule.MouseLook.CurrentHorizontal = rotation.eulerAngles.y;
    }

    /// <summary>
    /// Aims the camera at a world point through the native mouse-look. Yaw is derived from the
    /// horizontal delta; pitch is the elevation angle (+up), matching CurrentVertical's sign convention.
    /// </summary>
    public void LookAt(Vector3 worldPoint)
    {
        if (Hub.roleManager.CurrentRole is not IFpcRole fpc)
        {
            return;
        }

        FpcMouseLook mouseLook = fpc.FpcModule.MouseLook;
        Vector3 origin = Hub.PlayerCameraReference.position;
        Vector3 delta = worldPoint - origin;
        Vector3 horizontal = new(delta.x, 0f, delta.z);
        if (horizontal.sqrMagnitude >= 1e-6f)
        {
            mouseLook.CurrentHorizontal = Quaternion.LookRotation(horizontal, Vector3.up).eulerAngles.y;
        }

        mouseLook.CurrentVertical = Mathf.Atan2(delta.y, horizontal.magnitude) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// The point native hitreg actually tests: the victim's Body <see cref="HitboxIdentity"/> centre of
    /// mass (falling back to any hitbox, then to a geometric estimate). Live, so it tracks the model.
    /// </summary>
    public Vector3 AimPoint() => AimPoint(HitboxType.Body);

    /// <summary>
    /// The live centre of mass of a SPECIFIC hitbox — <see cref="HitboxType.Headshot"/> for a headshot,
    /// which the native <see cref="PlayerStatsSystem.FirearmDamageHandler"/> multiplies by 2. Hitbox
    /// choice is the ONLY lever here: the damage multiplier is applied by
    /// <see cref="HitboxIdentity.Damage"/> from the hitbox the ray actually struck, so aiming at the
    /// head is what produces a headshot — nothing about the shot itself is special-cased.
    /// Falls back through the other hitboxes, then to a geometric estimate, so it never returns nothing.
    /// </summary>
    public Vector3 AimPoint(HitboxType preferred)
    {
        HitboxIdentity? best = null;
        int bestRank = int.MaxValue;
        foreach (HitboxIdentity hitbox in HitboxIdentity.Instances)
        {
            if (hitbox == null || hitbox.TargetHub != Hub)
            {
                continue;
            }

            // Exact match first, then the biggest remaining box (Body > Limb > other).
            int rank = hitbox.HitboxType == preferred
                ? -1
                : hitbox.HitboxType switch
                {
                    HitboxType.Body => 0,
                    HitboxType.Limb => 1,
                    _ => 2,
                };
            if (rank < bestRank)
            {
                best = hitbox;
                bestRank = rank;
            }
        }

        if (best != null)
        {
            return best.CenterOfMass;
        }

        Transform camera = Hub.PlayerCameraReference;
        Vector3 feet = Hub.transform.position;
        // No hitboxes at all: the camera reference IS roughly eye level, so it is the head estimate.
        return preferred == HitboxType.Headshot
            ? (camera != null ? camera.position : feet + (Vector3.up * 1.65f))
            : Vector3.Lerp(feet, camera != null ? camera.position : feet + (Vector3.up * 1.65f), 0.55f);
    }

    /// <summary>True when this actor currently exposes a real head hitbox (so a headshot is aimable).</summary>
    public bool HasHeadHitbox() => HitboxIdentity.Instances
        .Any(hitbox => hitbox != null && hitbox.TargetHub == Hub && hitbox.HitboxType == HitboxType.Headshot);

    /// <summary>Adds an item natively; firearms also get reserve ammo so the native reload can fill them.</summary>
    public ItemBase? GiveItem(ItemType itemType)
    {
        try
        {
            Inventory inventory = Hub.inventory;
            ItemBase? item = inventory.ServerAddItem(itemType, ItemAddReason.AdminCommand);
            if (item is Firearm firearm && firearm.TryGetModule(out IPrimaryAmmoContainerModule ammo))
            {
                inventory.ServerAddAmmo(ammo.AmmoType, ReserveAmmo);
            }

            return item;
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] GiveItem({itemType}) for {Label} failed: {exception.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// Empties the inventory (and reserve ammo) natively so a deliberately-chosen weapon is the only
    /// one present. Without this, a role's native loadout decides which firearm gets equipped.
    /// </summary>
    public void ClearItems()
    {
        try
        {
            Player.Get(Hub)?.ClearInventory();
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] ClearItems for {Label} failed: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Sets both current and max health through the native <see cref="PlayerStatsSystem.HealthStat"/>.
    /// Setting MaxValue too keeps the HUD/ragdoll consistent — a CurValue above MaxValue reads as a
    /// full bar and native regen would claw it back.
    /// </summary>
    public void SetHealth(float health)
    {
        try
        {
            PlayerStatsSystem.HealthStat stat = Hub.playerStats.GetModule<PlayerStatsSystem.HealthStat>();
            stat.MaxValue = health;
            stat.CurValue = health;
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] SetHealth for {Label} failed: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>Selects an inventory item of the given type and waits its native draw time.</summary>
    public IEnumerator<float> Equip(ItemType itemType)
    {
        ItemBase? item = Hub.inventory.UserInventory.Items.Values.FirstOrDefault(i => i != null && i.ItemTypeId == itemType);
        if (item == null)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] {Label} has no {itemType} to equip.");
            yield break;
        }

        IEnumerator<float> body = EquipItem(item);
        while (body.MoveNext())
        {
            yield return body.Current;
        }
    }

    /// <summary>
    /// Equips the first firearm in the inventory (a role's native loadout weapon, whatever it is) and
    /// waits until its native trigger action actually exists. REQUIRED before firing: the
    /// "Shoot-&gt;Click" row is produced by <see cref="DummyKeyEmulator"/> from its registered listener
    /// list, and a firearm only registers ActionName.Shoot while it is EQUIPPED and polling input.
    /// An unequipped firearm has no trigger row at all — the exact reason a give-without-equip fires
    /// zero shots.
    /// </summary>
    public IEnumerator<float> EquipFirearm()
    {
        ItemBase? firearm = Hub.inventory.UserInventory.Items.Values.FirstOrDefault(i => i is Firearm);
        if (firearm == null)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] {Label} has no firearm in its inventory to equip.");
            yield break;
        }

        IEnumerator<float> body = EquipItem(firearm);
        while (body.MoveNext())
        {
            yield return body.Current;
        }

        // The listener registers on the module's first input poll, i.e. a frame or two AFTER the draw.
        float deadline = Time.timeSinceLevelLoad + 3f;
        while (Time.timeSinceLevelLoad < deadline && !HasDummyAction("Shoot->Click"))
        {
            yield return Timing.WaitForOneFrame;
        }

        if (!HasDummyAction("Shoot->Click"))
        {
            Logger.Warn(
                $"[ToyTricksDemo:Actor] {Label} equipped {firearm.ItemTypeId} but no 'Shoot->Click' action appeared " +
                "— the firearm is not registering ActionName.Shoot (is it still drawing?).");
        }
    }

    private IEnumerator<float> EquipItem(ItemBase item)
    {
        Hub.inventory.ServerSelectItem(item.ItemSerial);
        float deadline = Time.timeSinceLevelLoad + 4f;
        while (Hub.inventory.CurInstance != item && Time.timeSinceLevelLoad < deadline)
        {
            yield return Timing.WaitForOneFrame;
        }

        if (Hub.inventory.CurInstance != item)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] {Label} never equipped {item.ItemTypeId} (serial {item.ItemSerial}).");
            yield break;
        }

        // Native equipper draw time before the first trigger pull.
        yield return Timing.WaitForSeconds(1f);
    }

    /// <summary>True when the named native dummy action is currently offered for this hub.</summary>
    public bool HasDummyAction(string actionName)
    {
        if (Hub == null || Hub.gameObject == null || !Hub.IsDummy)
        {
            return false;
        }

        try
        {
            return DummyActionCollector.ServerGetActions(Hub)
                .Any(action => action.Action != null && string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] Action lookup on {Label} failed: {exception.GetBaseException().Message}");
            return false;
        }
    }

    /// <summary>
    /// Brings the held firearm to a fireable state through the NATIVE reload action only. Never writes
    /// Cocked/BoltLocked/AmmoStored: those are firearm-internal animation state, not a player input.
    /// </summary>
    public IEnumerator<float> ReadyFirearm()
    {
        if (Hub.inventory.CurInstance is not Firearm firearm)
        {
            yield break;
        }

        // Readiness is checked per weapon FAMILY, not just the automatic one. The revolver has no
        // AutomaticActionModule at all, so an automatic-only check returned "ready" for an EMPTY
        // cylinder and the shot silently dry-fired (observed 2026-07-25: fired=True, damage=0).
        bool ready;
        if (firearm.TryGetModule(out AutomaticActionModule action))
        {
            ready = (action.OpenBolt || action.AmmoStored > 0) && action.Cocked && !action.BoltLocked;
        }
        else if (firearm.TryGetModule(out IPrimaryAmmoContainerModule ammo))
        {
            // Cylinder (revolver) and pump (shotgun): a loaded container is the readiness signal;
            // cocking/pumping is handled by their own modules on the trigger pull.
            ready = ammo.AmmoStored > 0;
        }
        else
        {
            yield break;
        }

        if (ready)
        {
            yield break;
        }

        if (!TryInvokeDummyAction("Reload->Click"))
        {
            yield break;
        }

        float deadline = Time.timeSinceLevelLoad + 8f;
        bool sawReload = false;
        while (Time.timeSinceLevelLoad < deadline)
        {
            bool reloading = firearm.TryGetModule(out IReloaderModule reloader) && reloader.IsReloading;
            sawReload |= reloading;
            if (sawReload && !reloading)
            {
                break;
            }

            yield return Timing.WaitForSeconds(0.1f);
        }

        yield return Timing.WaitForSeconds(0.2f);

        // Loud rather than a silent dry fire: an empty weapon still produces fired=True with zero
        // damage, which reads as "the shot missed" and sends you hunting the wrong bug.
        if (firearm.TryGetModule(out IPrimaryAmmoContainerModule loaded) && loaded.AmmoStored <= 0)
        {
            Logger.Warn(
                $"[ToyTricksDemo:Actor] {Label}'s {firearm.ItemTypeId} is STILL EMPTY after the native reload " +
                $"(reserve {Hub.inventory.GetCurAmmo(loaded.AmmoType)}); the next shot will dry-fire.");
        }
    }

    /// <summary>
    /// Fires ONE native shot at a live aim point. Returns false when the trigger path is unavailable
    /// (no firearm equipped / action row missing), so the caller can log and carry on.
    /// </summary>
    public bool FireAt(Vector3 worldPoint)
    {
        LookAt(worldPoint);
        return TryInvokeDummyAction("Shoot->Click");
    }

    /// <summary>
    /// Invokes a native dummy action by name. The rows come from DummyActionCollector.ServerGetActions —
    /// the same registry the RA dummy panel renders — so invoking one is exactly a panel click.
    /// </summary>
    public bool TryInvokeDummyAction(string actionName)
    {
        if (Hub == null || Hub.gameObject == null || !Hub.IsDummy)
        {
            return false;
        }

        try
        {
            foreach (DummyAction action in DummyActionCollector.ServerGetActions(Hub))
            {
                if (action.Action != null && string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
                {
                    action.Action.Invoke();
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] Dummy action '{actionName}' on {Label} failed: {exception.GetBaseException().Message}");
        }

        return false;
    }

    /// <summary>
    /// Destroys the dummy. Reserve ammo is cleared first: native player teardown converts leftover
    /// reserve ammo into NEW world pickups that cannot be traced back afterwards (harness finding).
    /// </summary>
    public void Destroy()
    {
        ReferenceHub? hub = Hub;
        if (hub == null || hub.gameObject == null)
        {
            return;
        }

        try
        {
            hub.inventory?.UserInventory?.ReserveAmmo?.Clear();
            NetworkServer.Destroy(hub.gameObject);
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Actor] Teardown of {Label} failed: {exception.GetBaseException().Message}");
        }
    }
}
