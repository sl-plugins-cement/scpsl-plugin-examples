using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using PlayerRoles;
using PlayerRoles.Ragdolls;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// Dummies that WALK a patrol route on solid ground and shoot with a SHOTGUN — the counterpart to the
/// firefight bay's waypoint-carried, single-shot beat. Two things here are not demonstrated anywhere
/// else in the gallery:
///
/// 1. NATIVE LOCOMOTION. Nothing is teleported after placement. Each patroller's
///    <see cref="PlayerRoles.FirstPersonControl.FpcMotor.ReceivedPosition"/> is set to the next
///    waypoint, which is the same field a real client's movement lands in, so the game's own motor
///    walks them there with real speed, acceleration, collision and step-up. It is the arbitrary-target
///    form of the RA panel's "Walk forward 1.5m" dummy action.
/// 2. A SHOTGUN fired by a dummy, which is impossible on an unpatched server — see
///    <see cref="Patches.PumpActionModuleEquipUpdatePatch"/>. This bay is the live proof of that fix.
///
/// The scene is a continuous loop rather than a one-shot: two Chaos patrollers walk a lane and fire on
/// a rotating cast of Scientist targets, which respawn after each is downed.
/// </summary>
internal sealed class PatrolExhibit
{
    private const float StepSeconds = 1f / 15f;
    private const float WaypointReachedRadius = 1.1f;
    private const float FireIntervalSeconds = 1.6f;
    private const float EngageRange = 22f;
    private const float TargetRespawnDelay = 3.5f;

    private static readonly Color LaneColor = new(0.10f, 0.11f, 0.14f, 1f);
    private static readonly Color MarkerColor = new(0.85f, 0.45f, 0.10f, 1f);
    private static readonly Color TargetPadColor = new(0.12f, 0.36f, 0.58f, 1f);

    /// <summary>Patrol route in bay-local space; the walkers loop it forever.</summary>
    private static readonly Vector3[] RouteLocal =
    {
        new(-5.0f, 0f, -1.4f),
        new(5.0f, 0f, -1.4f),
        new(5.0f, 0f, 1.4f),
        new(-5.0f, 0f, 1.4f),
    };

    /// <summary>Where the Scientist targets stand, downrange of the patrol lane.</summary>
    private static readonly Vector3[] TargetPadsLocal =
    {
        new(-3.2f, 0f, 8.5f),
        new(0f, 0f, 9.5f),
        new(3.2f, 0f, 8.5f),
    };

    private readonly Transform _bay;
    private readonly Vector3 _bayOrigin;
    private readonly TextToy? _statusText;

    private readonly List<Walker> _walkers = new();
    private readonly List<DemoActor> _targets = new();
    private readonly HashSet<uint> _ownedRagdolls = new();

    private CoroutineHandle _routine;
    private bool _destroyed;
    private bool _refillPending;
    private int _kills;
    private string? _lastStatus;

    private sealed class Walker
    {
        public DemoActor Actor = null!;
        public int RouteIndex;
        public float NextFireAt;

        /// <summary>Progress watchdog: proves locomotion is actually happening, or says it is not.</summary>
        public Vector3 LastCheckedPosition;
        public float NextProgressCheckAt;
        public bool ReportedStall;
    }

    private PatrolExhibit(Transform bay, Vector3 bayOrigin, TextToy? statusText)
    {
        _bay = bay;
        _bayOrigin = bayOrigin;
        _statusText = statusText;
    }

    public static PatrolExhibit Build(Transform galleryRoot, Vector3 bayOffset, List<AdminToy> bag)
    {
        PrimitiveObjectToy bayRoot = ExhibitTools.InvisibleParent(bayOffset, Quaternion.identity, Vector3.one, galleryRoot, bag);
        Transform bay = bayRoot.Transform;

        ExhibitTools.Text(
            new Vector3(0f, 3.4f, 4.5f),
            3.2f,
            new Vector2(320f, 30f),
            "<align=center><size=12><b>WALKING PATROL  巡逻队</b></size>\n" +
            "<size=8>native FpcMotor locomotion · dummy-fired shotgun (patched)</size></align>",
            bay,
            bag);

        // Lane markings — cosmetic, and a visual reference for "they really are walking the route".
        ExhibitTools.Visible(new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(11.5f, 0.04f, 3.6f), LaneColor, bay, bag);
        foreach (Vector3 corner in RouteLocal)
        {
            ExhibitTools.Visible(corner + new Vector3(0f, 0.08f, 0f), Quaternion.identity, new Vector3(0.3f, 0.12f, 0.3f), MarkerColor, bay, bag);
        }

        foreach (Vector3 pad in TargetPadsLocal)
        {
            ExhibitTools.Visible(pad + new Vector3(0f, 0.03f, 0f), Quaternion.identity, new Vector3(1.1f, 0.06f, 1.1f), TargetPadColor, bay, bag);
        }

        TextToy status = ExhibitTools.Text(
            new Vector3(0f, 2.6f, 4.5f),
            1.6f,
            new Vector2(160f, 24f),
            "<align=center><size=10><color=#FFD166>{0}</color></size></align>",
            bay,
            bag);
        status.Arguments.Add("STARTING 启动中");

        return new PatrolExhibit(bay, bay.position, status);
    }

    public void Start()
    {
        if (_destroyed || _routine.IsRunning)
        {
            return;
        }

        _routine = Timing.RunCoroutine(RunPatrol());
    }

    public void StopAndDestroyPayloads()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        Timing.KillCoroutines(_routine);
        ClearActors();
    }

    private IEnumerator<float> RunPatrol()
    {
        // Park below the map for the first frame (a fresh hub sits at the world origin until placed).
        Vector3 park = _bay.TransformPoint(new Vector3(0f, -300f, 0f));

        for (int i = 0; i < 2; i++)
        {
            DemoActor? actor = DemoActor.TrySpawn($"TTD-PATROL{i + 1}", park + (Vector3.right * i));
            if (actor == null)
            {
                continue;
            }

            IEnumerator<float> role = actor.InitializeRole(RoleTypeId.ChaosConscript, withLoadout: true);
            while (role.MoveNext())
            {
                yield return role.Current;
            }

            if (_destroyed)
            {
                yield break;
            }

            // Start each walker at a different corner so they are visibly out of phase.
            int startIndex = i * 2;
            actor.PlaceAt(WorldRoute(startIndex) + (Vector3.up * 1f), Quaternion.identity);

            // The shotgun: only fires because of the PumpActionModule dummy patch. If the patch is off
            // the give still succeeds but the trigger does nothing, so say so rather than look broken.
            actor.ClearItems();
            actor.GiveItem(ItemType.GunShotgun);
            IEnumerator<float> draw = actor.EquipFirearm();
            while (draw.MoveNext())
            {
                yield return draw.Current;
            }

            _walkers.Add(new Walker { Actor = actor, RouteIndex = (startIndex + 1) % RouteLocal.Length });
        }

        if (_walkers.Count == 0)
        {
            SetStatus("SPAWN FAILED 失败");
            yield break;
        }

        if (!Patches.DummyFirearmPatches.IsApplied)
        {
            Logger.Warn(
                "[ToyTricksDemo:Patrol] patch_dummy_firearms is off, so the shotgun cannot fire from a " +
                "dummy (PumpActionModule gates its only shot-enqueue path on IsLocalPlayer). The patrol " +
                "will walk but never shoot.");
            SetStatus("WALK ONLY 仅巡逻");
        }

        IEnumerator<float> targets = SpawnTargets(park);
        while (targets.MoveNext())
        {
            yield return targets.Current;
        }

        while (!_destroyed)
        {
            StepWalkers();
            yield return Timing.WaitForSeconds(StepSeconds);
        }
    }

    /// <summary>Fills every empty target pad with a fresh 1 HP Scientist.</summary>
    private IEnumerator<float> SpawnTargets(Vector3 park)
    {
        for (int i = _targets.Count; i < TargetPadsLocal.Length && !_destroyed; i++)
        {
            DemoActor? target = DemoActor.TrySpawn($"TTD-TGT{i + 1}", park + (Vector3.forward * (i + 1)));
            if (target == null)
            {
                continue;
            }

            IEnumerator<float> role = target.InitializeRole(RoleTypeId.Scientist, withLoadout: false);
            while (role.MoveNext())
            {
                yield return role.Current;
            }

            if (_destroyed)
            {
                yield break;
            }

            Vector3 pad = _bay.TransformPoint(TargetPadsLocal[i]);
            // Face the patrol lane so the targets read as facing the shooters.
            target.PlaceAt(pad + (Vector3.up * 1f), Quaternion.LookRotation(Vector3.back, Vector3.up));
            // 1 HP: one pellet is enough, so the loop stays brisk and does not depend on buckshot
            // pellet-count/falloff at this range.
            target.SetHealth(1f);
            _targets.Add(target);
        }
    }

    /// <summary>
    /// One tick of the patrol: advance each walker toward its next route corner through the NATIVE
    /// motor, and take a shot when a live target is in range and the cooldown has elapsed.
    /// </summary>
    private void StepWalkers()
    {
        foreach (Walker walker in _walkers)
        {
            DemoActor actor = walker.Actor;
            if (!actor.IsAlive)
            {
                continue;
            }

            Vector3 destination = WorldRoute(walker.RouteIndex);
            if (actor.FlatDistanceTo(destination) <= WaypointReachedRadius)
            {
                walker.RouteIndex = (walker.RouteIndex + 1) % RouteLocal.Length;
                destination = WorldRoute(walker.RouteIndex);
            }

            // Native locomotion: WalkTowards feeds the motor a nearby stepped target every tick (a
            // distant one is rejected as desync and pins the dummy — see WalkTowards). No teleporting.
            actor.WalkTowards(destination);
            CheckProgress(walker);

            DemoActor? target = NearestLiveTarget(actor);
            if (target == null)
            {
                // Nothing to shoot: face the direction of travel so they read as patrolling.
                Vector3 heading = destination - actor.Position;
                if (heading.sqrMagnitude > 0.01f)
                {
                    actor.LookAlong(Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.z), Vector3.up));
                }

                continue;
            }

            // Track the target while walking — the aim and the locomotion are independent.
            actor.LookAt(target.AimPoint());
            if (Time.timeSinceLevelLoad < walker.NextFireAt)
            {
                continue;
            }

            walker.NextFireAt = Time.timeSinceLevelLoad + FireIntervalSeconds;
            actor.FireAt(target.AimPoint());
        }

        SweepDownedTargets();
    }

    /// <summary>
    /// Reports ONCE per walker if it is being told to walk but is not actually moving. Silence here is
    /// the pass condition; without it a stalled motor looks identical to a working one that simply has
    /// nothing to do, which is how the first patrol build shipped broken.
    /// </summary>
    private static void CheckProgress(Walker walker)
    {
        float now = Time.timeSinceLevelLoad;
        if (now < walker.NextProgressCheckAt)
        {
            return;
        }

        if (walker.NextProgressCheckAt > 0f && !walker.ReportedStall)
        {
            float moved = Vector3.Distance(walker.Actor.Position, walker.LastCheckedPosition);
            if (moved < 0.05f)
            {
                walker.ReportedStall = true;
                Logger.Warn(
                    $"[ToyTricksDemo:Patrol] {walker.Actor.Label} is NOT walking (moved {moved:0.###} m in 2 s " +
                    $"while ordered toward route corner {walker.RouteIndex}). The native motor rejects targets " +
                    "further than ~one tick of max speed; check WalkTowards' step size and floor snapping.");
            }
        }

        walker.LastCheckedPosition = walker.Actor.Position;
        walker.NextProgressCheckAt = now + 2f;
    }

    private DemoActor? NearestLiveTarget(DemoActor shooter)
    {
        DemoActor? best = null;
        float bestDistance = EngageRange;
        foreach (DemoActor target in _targets)
        {
            if (!target.IsAlive)
            {
                continue;
            }

            float distance = Vector3.Distance(shooter.Position, target.Position);
            if (distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Retires downed targets and schedules a refill so the range never runs dry.</summary>
    private void SweepDownedTargets()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            DemoActor target = _targets[i];
            if (target.IsAlive)
            {
                continue;
            }

            _kills++;
            CollectRagdollsFor(target);
            target.Destroy();
            _targets.RemoveAt(i);
        }

        SetStatus($"PATROL 巡逻中 · {_kills} 命中");
        if (_targets.Count < TargetPadsLocal.Length && !_destroyed && !_refillPending)
        {
            _refillPending = true;
            Timing.CallDelayed(TargetRespawnDelay, () =>
            {
                _refillPending = false;
                if (_destroyed)
                {
                    return;
                }

                DestroyOwnedRagdolls();
                Timing.RunCoroutine(SpawnTargets(_bay.TransformPoint(new Vector3(0f, -300f, 0f))));
            });
        }
    }

    /// <summary>
    /// A route corner in world space, snapped down onto whatever floor is actually there. The bay root
    /// is placed on the floor under the viewer, but the patrol lane is several meters deep and the
    /// ground can slope or step; walking toward a target buried in (or floating above) the floor makes
    /// the motor treat it as a climb/fall instead of a walk.
    /// </summary>
    private Vector3 WorldRoute(int index)
    {
        Vector3 point = _bay.TransformPoint(RouteLocal[index % RouteLocal.Length]);
        return Physics.Raycast(point + (Vector3.up * 1.5f), Vector3.down, out RaycastHit hit, 6f,
            PlayerRoles.FirstPersonControl.FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : point;
    }

    private void SetStatus(string text)
    {
        if (string.Equals(_lastStatus, text, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (_statusText is { IsDestroyed: false } && _statusText.Arguments.Count > 0)
            {
                _statusText.Arguments[0] = text;
                _lastStatus = text;
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Patrol] Status update failed: {exception.GetBaseException().Message}");
        }
    }

    private void CollectRagdollsFor(DemoActor actor)
    {
        foreach (BasicRagdoll ragdoll in RagdollManager.AllRagdolls)
        {
            if (ragdoll != null && ragdoll.gameObject != null && ragdoll.Info.OwnerHub == actor.Hub)
            {
                _ownedRagdolls.Add(ragdoll.netId);
            }
        }
    }

    private void DestroyOwnedRagdolls()
    {
        if (_ownedRagdolls.Count == 0)
        {
            return;
        }

        foreach (BasicRagdoll ragdoll in RagdollManager.AllRagdolls.ToArray())
        {
            if (ragdoll == null || ragdoll.gameObject == null || !_ownedRagdolls.Contains(ragdoll.netId))
            {
                continue;
            }

            try
            {
                NetworkServer.Destroy(ragdoll.gameObject);
            }
            catch (Exception exception)
            {
                Logger.Warn($"[ToyTricksDemo:Patrol] Ragdoll cleanup failed: {exception.GetBaseException().Message}");
            }
        }

        _ownedRagdolls.Clear();
    }

    private void ClearActors()
    {
        foreach (DemoActor target in _targets)
        {
            CollectRagdollsFor(target);
        }

        foreach (Walker walker in _walkers)
        {
            CollectRagdollsFor(walker.Actor);
        }

        DestroyOwnedRagdolls();

        foreach (DemoActor target in _targets)
        {
            target.Destroy();
        }

        foreach (Walker walker in _walkers)
        {
            walker.Actor.Destroy();
        }

        _targets.Clear();
        _walkers.Clear();
    }
}
