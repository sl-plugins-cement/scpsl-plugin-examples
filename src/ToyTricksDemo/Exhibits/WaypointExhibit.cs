using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using RelativePositioning;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// A moving WaypointToy carrier that demonstrates native relative positioning for an FPC dummy and a
/// real pickup. Neither payload is manually parented; FirstPersonMovementModule and PickupStandardPhysics
/// select the waypoint while the carrier is stationary, then follow it through the native waypoint path.
/// The dummy's spawn/role/placement dance goes through <see cref="DemoActor"/>, the same helper the
/// firefight and patrol bays use.
/// </summary>
internal sealed class WaypointExhibit
{
    private const float AssociationTimeout = 1.5f;
    private const float CycleSeconds = 8f;
    // Sparse server keyframes; the client's MovementSmoothing lerp still runs every rendered frame.
    private const float AnimationStepSeconds = 1f / 15f;

    // Player.Position is the FPC capsule CENTRE, ~1 m above the feet — an on-platform payload needs
    // roughly +1 above the platform surface (platform top is y=0 in the waypoint frame), or the dummy
    // spawns waist-deep in the deck.
    private static readonly Vector3 DummyLocalPosition = new(-0.55f, 1.0f, 0f);
    private static readonly Vector3 PickupLocalPosition = new(0.55f, 0.22f, 0f);

    private readonly WaypointToy _waypoint;
    private readonly AdminToyWaypoint _targetWaypoint;
    private readonly Vector3 _anchorLocalPosition;

    private DemoActor? _dummy;
    private Pickup? _pickup;
    private CoroutineHandle _routine;
    private bool _destroyed;

    private WaypointExhibit(WaypointToy waypoint, AdminToyWaypoint targetWaypoint, Vector3 anchorLocalPosition)
    {
        _waypoint = waypoint;
        _targetWaypoint = targetWaypoint;
        _anchorLocalPosition = anchorLocalPosition;
    }

    public static WaypointExhibit Build(Transform galleryRoot, Vector3 bayOffset, List<AdminToy> bag)
    {
        ExhibitTools.Text(
            bayOffset + new Vector3(0f, 2.55f, 0f),
            2.0f,
            new Vector2(330f, 28f),
            "<align=\"center\"><b>WAYPOINT CARRIER  路点载台</b>\n<size=70%>native FPC + pickup relative positioning</size></align>",
            galleryRoot,
            bag);

        Vector3 anchor = bayOffset + new Vector3(0f, 1.05f, 0f);
        WaypointToy waypoint = WaypointToy.Create(anchor, Quaternion.identity, Vector3.one, galleryRoot, networkSpawn: false);
        AdminToyWaypoint targetWaypoint = waypoint.Base.GetComponent<AdminToyWaypoint>()
            ?? waypoint.Base.GetComponentInChildren<AdminToyWaypoint>(includeInactive: true)
            ?? throw new InvalidOperationException("WaypointToy prefab has no AdminToyWaypoint component.");
        waypoint.IsStatic = false;
        waypoint.BoundsSize = new Vector3(3f, 3f, 2.2f);
        waypoint.PriorityBias = 20f;
        waypoint.VisualizeBounds = true;
        // RAW value on the Base — see the 256-invert trap on ExhibitTools.RawMovementSmoothing.
        waypoint.Base.NetworkMovementSmoothing = ExhibitTools.RawMovementSmoothing;
        waypoint.SyncInterval = AnimationStepSeconds;
        waypoint.Spawn();
        bag.Add(waypoint);

        ExhibitTools.Visible(
            new Vector3(0f, -0.075f, 0f),
            Quaternion.identity,
            new Vector3(2.4f, 0.15f, 1.25f),
            new Color(0.08f, 0.48f, 0.72f, 1f),
            waypoint.Transform,
            bag,
            flags: PrimitiveFlags.Visible | PrimitiveFlags.Collidable);

        ExhibitTools.Text(
            new Vector3(0f, 0.62f, -0.64f),
            1.4f,
            new Vector2(300f, 26f),
            "<align=\"center\"><b>TUTORIAL FPC DUMMY + LOCKED FLASHLIGHT</b>\n<size=65%>payloads are not Transform-parented by this plugin</size></align>",
            waypoint.Transform,
            bag);

        return new WaypointExhibit(waypoint, targetWaypoint, anchor);
    }

    public void Start()
    {
        if (_destroyed || _routine.IsRunning)
        {
            return;
        }

        _routine = Timing.RunCoroutine(InitializeAndMove());
    }

    public void StopAndDestroyPayloads()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        Timing.KillCoroutines(_routine);
        DestroyPickup();
        DestroyDummy();
    }

    private IEnumerator<float> InitializeAndMove()
    {
        // DemoActor owns the spawn/role dance: park far below the platform, deferred role set (the
        // UserId race), then poll until the FPC module actually exists — no fixed sleeps.
        _dummy = DemoActor.TrySpawn(
            "TTD-WAYPOINT",
            _waypoint.Transform.TransformPoint(DummyLocalPosition + (Vector3.down * 300f)));
        if (!CanContinue())
        {
            yield break;
        }

        if (_dummy != null)
        {
            IEnumerator<float> role = _dummy.InitializeRole(RoleTypeId.Tutorial, withLoadout: false);
            while (role.MoveNext())
            {
                yield return role.Current;
            }

            if (!CanContinue())
            {
                yield break;
            }

            if (_dummy.IsAlive)
            {
                _dummy.ClearItems();
                _dummy.PlaceAt(_waypoint.Transform.TransformPoint(DummyLocalPosition), _waypoint.Transform.rotation);
            }
            else
            {
                Logger.Warn("[ToyTricksDemo:Waypoint] Dummy role initialization failed; continuing with the pickup only.");
                DestroyDummy();
            }
        }

        TrySpawnPickup();

        float associationDeadline = Time.timeSinceLevelLoad + AssociationTimeout;
        while (CanContinue()
               && Time.timeSinceLevelLoad < associationDeadline
               && !AreSurvivingPayloadsAssociated())
        {
            yield return Timing.WaitForOneFrame;
        }

        if (!CanContinue())
        {
            yield break;
        }

        RemoveUnassociatedPayloads();
        if (_dummy == null && (_pickup == null || _pickup.IsDestroyed))
        {
            Logger.Warn("[ToyTricksDemo:Waypoint] Both payloads failed to associate; leaving the carrier stationary.");
            yield break;
        }

        float startedAt = Time.timeSinceLevelLoad;
        while (CanContinue())
        {
            float phase = ((Time.timeSinceLevelLoad - startedAt) / CycleSeconds) * Mathf.PI * 2f;
            Vector3 offset = new(
                1.2f * Mathf.Sin(phase),
                0.15f * Mathf.Sin(phase * 2f),
                0.4f * (1f - Mathf.Cos(phase)));
            float yaw = 15f * Mathf.Sin(phase);

            _waypoint.Position = _anchorLocalPosition + offset;
            _waypoint.Rotation = Quaternion.Euler(0f, yaw, 0f);
            yield return Timing.WaitForSeconds(AnimationStepSeconds);
        }
    }

    private bool CanContinue() => !_destroyed && _waypoint != null && !_waypoint.IsDestroyed;

    private bool AreSurvivingPayloadsAssociated()
    {
        bool dummyReady = _dummy == null || _dummy.IsOnWaypoint(_targetWaypoint);
        bool pickupReady = _pickup == null || _pickup.IsDestroyed || IsPickupOnTargetWaypoint();
        return dummyReady && pickupReady;
    }

    private bool IsPickupOnTargetWaypoint()
        => _pickup != null
            && !_pickup.IsDestroyed
            && _pickup.PickupStandardPhysics != null
            && _pickup.Transform.parent == _targetWaypoint.transform;

    private void RemoveUnassociatedPayloads()
    {
        if (_dummy != null && !_dummy.IsOnWaypoint(_targetWaypoint))
        {
            Logger.Warn("[ToyTricksDemo:Waypoint] Dummy did not select this waypoint before timeout; removing it.");
            DestroyDummy();
        }

        if (_pickup != null && !_pickup.IsDestroyed && !IsPickupOnTargetWaypoint())
        {
            Logger.Warn("[ToyTricksDemo:Waypoint] Flashlight did not select this waypoint before timeout; removing it.");
            DestroyPickup();
        }
    }

    private void TrySpawnPickup()
    {
        try
        {
            Vector3 worldPosition = _waypoint.Transform.TransformPoint(PickupLocalPosition);
            _pickup = Pickup.Create(ItemType.Flashlight, worldPosition, _waypoint.Transform.rotation, Vector3.one, networkSpawn: false);
            if (_pickup == null)
            {
                throw new InvalidOperationException("Pickup.Create returned null for ItemType.Flashlight.");
            }

            _pickup.IsLocked = true;
            _pickup.Spawn();
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Waypoint] Flashlight spawn failed: {exception.GetBaseException().Message}");
            DestroyPickup();
        }
    }

    private void DestroyPickup()
    {
        Pickup? pickup = _pickup;
        _pickup = null;
        if (pickup == null)
        {
            return;
        }

        try
        {
            if (!pickup.IsDestroyed)
            {
                pickup.Destroy();
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Waypoint] Pickup teardown failed: {exception.GetBaseException().Message}");
        }
    }

    private void DestroyDummy()
    {
        DemoActor? dummy = _dummy;
        _dummy = null;
        dummy?.Destroy();
    }
}
