using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using PlayerRoles;
using PlayerRoles.Ragdolls;
using RelativePositioning;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// A staged firefight between two animated RA dummies on two independently moving WaypointToy
/// platforms: a Chaos Insurgency shooter tracks and fires on a Scientist riding the far tram, both
/// carried by native relative positioning (nothing is Transform-parented, so hitreg, backtracking, and
/// the client-side lerp are all the game's own). An InteractableToy button on the near plinth plays and
/// resets the scene.
///
/// The three things this exhibit demonstrates that the plain waypoint bay does not:
///
/// 1. TWO live waypoint carriers with FPC payloads moving at once, with the shooter tracking a target
///    whose world position is being driven by a DIFFERENT carrier — the case where a naive static aim
///    point misses every shot.
/// 2. Real native damage across that motion: the shooter uses the "Shoot-&gt;Click" dummy action and the
///    victim's live Body hitbox centre of mass, so the shots run the actual server firearm pipeline
///    (backtrack, hitreg, hitmarkers, ragdoll) rather than a scripted health subtraction.
/// 3. An InteractableToy press-button as the exhibit's own control surface (InteractionDuration = 0 =&gt;
///    instant Interact-key press, PlayerEvents.InteractedToy), so the whole scene is replayable in-game
///    without going back to the RA console.
///
/// FACTION NOTE: ChaosConscript (FoundationEnemy) vs Scientist (FoundationStaff) are natural enemies,
/// so HitboxIdentity.IsDamageable passes with friendly fire OFF — no server FF config change needed.
/// </summary>
internal sealed class FirefightExhibit
{
    // Sparse server keyframes; the client's MovementSmoothing lerp still runs every rendered frame.
    private const float AnimationStepSeconds = 1f / 15f;
    private const float ShooterCycleSeconds = 11f;
    private const float TargetCycleSeconds = 7.5f;

    // Payload placement is in the carrier's LOCAL frame, where the platform deck top is y = 0.
    // Player.Position is the FPC capsule CENTRE (~1 m above the feet), so a dummy standing on the deck
    // sits at y ≈ +1 — at y = 0 it spawns waist-deep in its own platform.
    private static readonly Vector3 PayloadLocalPosition = new(0f, 1.0f, 0f);

    private const float ShooterLaneZ = 0f;
    private const float TargetLaneZ = 7.5f;
    private const float DeckHeight = 1.15f;

    private static readonly Color ShooterDeck = new(0.52f, 0.14f, 0.10f, 1f);
    private static readonly Color TargetDeck = new(0.10f, 0.34f, 0.58f, 1f);
    private static readonly Color RailColor = new(0.7f, 0.72f, 0.78f, 1f);
    private static readonly Color ButtonIdle = new(0.85f, 0.62f, 0.12f, 1f);
    private static readonly Color ButtonActive = new(0.16f, 0.78f, 0.32f, 1f);
    private static readonly Color PlinthColor = new(0.07f, 0.075f, 0.09f, 1f);

    private readonly Transform _bay;

    private readonly WaypointToy _shooterCarrier;
    private readonly WaypointToy _targetCarrier;
    private readonly AdminToyWaypoint _shooterWaypoint;
    private readonly AdminToyWaypoint _targetWaypoint;
    private readonly Vector3 _shooterAnchor;
    private readonly Vector3 _targetAnchor;

    private readonly InteractableToy _button;
    private readonly PrimitiveObjectToy _buttonCap;
    private readonly TextToy _statusText;

    private DemoActor? _shooter;
    private DemoActor? _target;
    private CoroutineHandle _scene;
    private CoroutineHandle _carriers;
    private readonly HashSet<uint> _ownedRagdolls = new();
    private bool _destroyed;
    private bool _running;
    private float _lastPressAt = float.NegativeInfinity;

    /// <summary>
    /// Which hitbox the last shot actually struck, observed from the native damage handler rather than
    /// assumed from the aim. Null until a shot lands. A 1 HP target can die outright, so this is
    /// captured from BOTH Hurt and Death — Hurt does not fire for a killing blow on every path.
    /// </summary>
    private HitboxType? _lastHitbox;

    private FirefightExhibit(
        Transform bay,
        WaypointToy shooterCarrier,
        WaypointToy targetCarrier,
        AdminToyWaypoint shooterWaypoint,
        AdminToyWaypoint targetWaypoint,
        Vector3 shooterAnchor,
        Vector3 targetAnchor,
        InteractableToy button,
        PrimitiveObjectToy buttonCap,
        TextToy statusText)
    {
        _bay = bay;
        _shooterCarrier = shooterCarrier;
        _targetCarrier = targetCarrier;
        _shooterWaypoint = shooterWaypoint;
        _targetWaypoint = targetWaypoint;
        _shooterAnchor = shooterAnchor;
        _targetAnchor = targetAnchor;
        _button = button;
        _buttonCap = buttonCap;
        _statusText = statusText;
    }

    public static FirefightExhibit Build(Transform galleryRoot, Vector3 bayOffset, List<AdminToy> bag)
    {
        PrimitiveObjectToy bayRoot = ExhibitTools.InvisibleParent(bayOffset, Quaternion.identity, Vector3.one, galleryRoot, bag);
        Transform bay = bayRoot.Transform;

        ExhibitTools.Text(
            new Vector3(0f, 3.55f, TargetLaneZ * 0.5f),
            3.0f,
            new Vector2(300f, 30f),
            "<align=center><size=12><b>MOVING FIREFIGHT  移动交火</b></size>\n" +
            "<size=8>two waypoint carriers · native aim + native trigger · real damage</size></align>",
            bay,
            bag);

        Vector3 shooterAnchor = new(0f, DeckHeight, ShooterLaneZ);
        Vector3 targetAnchor = new(0f, DeckHeight, TargetLaneZ);
        WaypointToy shooterCarrier = CreateCarrier(shooterAnchor, bay, bag, ShooterDeck, "CHAOS 混沌", out AdminToyWaypoint shooterWaypoint);
        WaypointToy targetCarrier = CreateCarrier(targetAnchor, bay, bag, TargetDeck, "SCIENTIST 科学家", out AdminToyWaypoint targetWaypoint);

        // Control plinth on the viewer's side of the near lane, out of the shooter's firing lane.
        Vector3 plinth = new(-2.6f, 0f, -1.5f);
        ExhibitTools.Visible(plinth + new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(0.7f, 0.9f, 0.7f), PlinthColor, bay, bag);

        // The button: an invisible box collider (InteractionDuration = 0 => instant Interact press)
        // plus a visible cap of the same footprint so players can see the thing they are pressing.
        Vector3 buttonLocal = plinth + new Vector3(0f, 0.95f, 0f);
        InteractableToy button = ExhibitTools.Button(buttonLocal, Quaternion.identity, new Vector3(0.42f, 0.24f, 0.42f), bay, bag);
        PrimitiveObjectToy buttonCap = ExhibitTools.Visible(
            buttonLocal, Quaternion.identity, new Vector3(0.34f, 0.14f, 0.34f), ButtonIdle, bay, bag,
            PrimitiveType.Cylinder);

        TextToy statusText = ExhibitTools.Text(
            plinth + new Vector3(0f, 1.55f, 0f),
            1.3f,
            new Vector2(150f, 26f),
            "<align=center><size=9><b>PRESS  按下</b></size>\n<size=11><color=#FFD166>{0}</color></size></align>",
            bay,
            bag);
        statusText.Arguments.Add("READY 就绪");

        return new FirefightExhibit(
            bay, shooterCarrier, targetCarrier, shooterWaypoint, targetWaypoint,
            shooterAnchor, targetAnchor, button, buttonCap, statusText);
    }

    /// <summary>
    /// One tram: a non-static WaypointToy (so AdminToyWaypoint records backtrack frames and pushes
    /// OnMoved to its children) with a solid collidable deck, side rails, and a lane label.
    /// </summary>
    private static WaypointToy CreateCarrier(
        Vector3 anchor, Transform bay, List<AdminToy> bag, Color deckColor, string label, out AdminToyWaypoint waypoint)
    {
        WaypointToy carrier = WaypointToy.Create(anchor, Quaternion.identity, Vector3.one, bay, networkSpawn: false);
        waypoint = carrier.Base.GetComponent<AdminToyWaypoint>()
            ?? carrier.Base.GetComponentInChildren<AdminToyWaypoint>(includeInactive: true)
            ?? throw new InvalidOperationException("WaypointToy prefab has no AdminToyWaypoint component.");

        carrier.IsStatic = false;
        // The bounds are the SELECTION volume: AdminToyWaypoint.SqrDistanceTo returns MaxValue for
        // anything outside them, so a payload only binds to this carrier while inside this box.
        carrier.BoundsSize = new Vector3(3.4f, 3.4f, 3.4f);
        carrier.PriorityBias = 20f;
        carrier.VisualizeBounds = false;
        // RAW value on the Base — see the 256-invert trap on ExhibitTools.RawMovementSmoothing.
        carrier.Base.NetworkMovementSmoothing = ExhibitTools.RawMovementSmoothing;
        carrier.SyncInterval = AnimationStepSeconds;
        carrier.Spawn();
        bag.Add(carrier);

        Transform deck = carrier.Transform;
        ExhibitTools.Visible(
            new Vector3(0f, -0.075f, 0f), Quaternion.identity, new Vector3(2.6f, 0.15f, 1.6f), deckColor, deck, bag,
            flags: PrimitiveFlags.Visible | PrimitiveFlags.Collidable);
        ExhibitTools.Visible(new Vector3(0f, 0.28f, 0.82f), Quaternion.identity, new Vector3(2.6f, 0.6f, 0.06f), RailColor, deck, bag);
        ExhibitTools.Visible(new Vector3(0f, 0.28f, -0.82f), Quaternion.identity, new Vector3(2.6f, 0.6f, 0.06f), RailColor, deck, bag);
        ExhibitTools.Text(
            new Vector3(0f, 0.95f, -0.85f), 1.1f, new Vector2(120f, 18f),
            $"<align=center><size=10><b>{label}</b></size></align>", deck, bag);
        return carrier;
    }

    public void Start()
    {
        if (_destroyed)
        {
            return;
        }

        PlayerEvents.InteractedToy += OnInteractedToy;
        PlayerEvents.Hurt += OnPlayerHurt;
        PlayerEvents.Death += OnPlayerDeath;
        // The carriers move from the moment the bay exists, so the exhibit reads as live machinery
        // before anyone presses the button; only the actors wait for a press.
        _carriers = Timing.RunCoroutine(AnimateCarriers());
        SetStatus("READY 就绪 — press 按钮", ButtonIdle);
    }

    /// <summary>Stops everything and removes the dummies + any ragdolls this exhibit created.</summary>
    public void StopAndDestroyPayloads()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        PlayerEvents.InteractedToy -= OnInteractedToy;
        PlayerEvents.Hurt -= OnPlayerHurt;
        PlayerEvents.Death -= OnPlayerDeath;
        Timing.KillCoroutines(_scene);
        Timing.KillCoroutines(_carriers);
        ClearActors();
    }

    /// <summary>
    /// Records which hitbox this exhibit's target was struck on. HitboxIdentity.Damage stamps
    /// StandardDamageHandler.Hitbox with the box the ray actually hit before dealing damage, so this is
    /// the authoritative answer to "was it really a headshot?" — the aim only states an intent.
    /// </summary>
    private void OnPlayerHurt(PlayerHurtEventArgs ev) => CaptureHitbox(ev.Player, ev.DamageHandler);

    private void OnPlayerDeath(PlayerDeathEventArgs ev) => CaptureHitbox(ev.Player, ev.DamageHandler);

    private void CaptureHitbox(Player? victim, PlayerStatsSystem.DamageHandlerBase handler)
    {
        if (_destroyed
            || _target == null
            || victim == null
            || victim.ReferenceHub != _target.Hub
            || handler is not PlayerStatsSystem.StandardDamageHandler standard)
        {
            return;
        }

        _lastHitbox = standard.Hitbox;
    }

    private void OnInteractedToy(PlayerInteractedToyEventArgs ev)
    {
        if (_destroyed || _button.IsDestroyed || ev.Interactable.Base != _button.Base)
        {
            return;
        }

        // Debounce: the native interact route can deliver a press per client frame while the key is
        // held, and each one would otherwise restart the scene.
        if (Time.timeSinceLevelLoad - _lastPressAt < 0.75f)
        {
            return;
        }

        _lastPressAt = Time.timeSinceLevelLoad;
        Timing.KillCoroutines(_scene);
        ClearActors();
        _scene = Timing.RunCoroutine(RunScene(ev.Player?.Nickname ?? "unknown"));
    }

    /// <summary>
    /// Spawns both dummies, lets them bind to their carriers through native waypoint selection, then
    /// runs the shooter's aim-and-fire loop until the target dies or the beat times out.
    /// </summary>
    private IEnumerator<float> RunScene(string pressedBy)
    {
        _running = true;
        _lastHitbox = null;
        SetStatus("SPAWNING 生成中", ButtonActive);
        Logger.Info($"[ToyTricksDemo:Firefight] Scene started by {pressedBy}.");

        // Park both dummies far below the map for their first frame: a fresh hub sits at the world
        // origin until placed, which would flash a body in the middle of the facility.
        Vector3 park = _bay.TransformPoint(new Vector3(0f, -300f, 0f));
        _shooter = DemoActor.TrySpawn("TTD-CHAOS", park);
        _target = DemoActor.TrySpawn("TTD-SCI", park + Vector3.right);
        if (_shooter == null || _target == null)
        {
            SetStatus("SPAWN FAILED 失败", ButtonIdle);
            ClearActors();
            _running = false;
            yield break;
        }

        // ChaosConscript keeps its native loadout (that is where the firearm comes from); the
        // Scientist is stripped so it cannot shoot back and stays a clean damage target.
        IEnumerator<float> shooterRole = _shooter.InitializeRole(RoleTypeId.ChaosConscript, withLoadout: true);
        while (shooterRole.MoveNext())
        {
            yield return shooterRole.Current;
        }

        IEnumerator<float> targetRole = _target.InitializeRole(RoleTypeId.Scientist, withLoadout: false);
        while (targetRole.MoveNext())
        {
            yield return targetRole.Current;
        }

        if (!CanContinue())
        {
            yield break;
        }

        PlaceOnCarrier(_shooter, _shooterCarrier, faceZ: 1f);
        PlaceOnCarrier(_target, _targetCarrier, faceZ: -1f);

        // Native waypoint binding is not instant: FirstPersonMovementModule picks the closest waypoint
        // whose bounds contain it on a later frame. Until that happens the dummy is standing in world
        // space and the moving deck would slide out from under it.
        SetStatus("BINDING 绑定中", ButtonActive);
        float bindDeadline = Time.timeSinceLevelLoad + 2.5f;
        while (CanContinue()
               && Time.timeSinceLevelLoad < bindDeadline
               && !(IsOnWaypoint(_shooter, _shooterWaypoint) && IsOnWaypoint(_target, _targetWaypoint)))
        {
            yield return Timing.WaitForOneFrame;
        }

        if (!CanContinue())
        {
            yield break;
        }

        if (!IsOnWaypoint(_shooter, _shooterWaypoint) || !IsOnWaypoint(_target, _targetWaypoint))
        {
            // Not fatal for the demo — the dummies simply will not ride the platforms. Say so loudly
            // instead of silently showing a broken scene.
            Logger.Warn(
                "[ToyTricksDemo:Firefight] A payload did not bind to its carrier within the timeout " +
                $"(shooter={IsOnWaypoint(_shooter, _shooterWaypoint)}, target={IsOnWaypoint(_target, _targetWaypoint)}); " +
                "the firefight will still run but the unbound dummy will not ride its platform.");
        }

        // The one-shot kill comes from the TARGET, not the weapon: a 1 HP Scientist dies to any single
        // hit, so the beat does not depend on per-weapon damage values or on the ray finding the head.
        _target.SetHealth(1f);

        // Weapon selection. Only AutomaticActionModule firearms (AK/E-11/COM/Crossvec/FSP-9/Logicer)
        // work on an UNPATCHED server; the revolver and shotgun need the DummyFirearmPatches fixes for
        // the native bugs described there. Falls back to the loadout AK when those are unavailable, so
        // a config with patches off can never produce the crash or the silent no-op.
        ItemType weapon = ResolveWeapon();
        if (weapon != ItemType.None)
        {
            _shooter.ClearItems();
            _shooter.GiveItem(weapon);
        }

        // Equip BEFORE readiness/firing. The native "Shoot->Click" row does not exist for an
        // unequipped weapon: DummyKeyEmulator only offers rows for ActionName values a module has
        // registered as a listener, and a firearm registers ActionName.Shoot only while it is the held
        // item polling input. Giving the loadout is not enough — that was the 0-shot bug.
        SetStatus("DRAWING 拔枪", ButtonActive);
        IEnumerator<float> draw = _shooter.EquipFirearm();
        while (draw.MoveNext())
        {
            yield return draw.Current;
        }

        IEnumerator<float> ready = _shooter.ReadyFirearm();
        while (ready.MoveNext())
        {
            yield return ready.Current;
        }

        // ONE SHOT, to the head. Aim is settled BEFORE the trigger rather than in the same frame:
        // FpcMouseLook applies on the server's next rotation update, and the shot's own backtrack
        // resolves against where the target was a moment ago — with both carriers moving, firing on
        // the same frame as the aim write is what turns a head hit into a body graze.
        SetStatus("AIMING 瞄准", ButtonActive);
        float settleUntil = Time.timeSinceLevelLoad + 0.6f;
        while (CanContinue() && _target.IsAlive && Time.timeSinceLevelLoad < settleUntil)
        {
            _shooter.LookAt(_target.AimPoint(HitboxType.Headshot));
            yield return Timing.WaitForOneFrame;
        }

        if (!CanContinue())
        {
            yield break;
        }

        if (!_target.HasHeadHitbox())
        {
            // Not fatal — AimPoint falls back to the best available box — but the 2x multiplier is
            // what makes this a one-shot, so say why it stopped being one.
            Logger.Warn(
                "[ToyTricksDemo:Firefight] Target exposes no Headshot hitbox; the shot will land on a " +
                "fallback box and may not be lethal in one hit.");
        }

        SetStatus("FIRING 开火", ButtonActive);
        float healthBefore = _target.Health;
        Vector3 headPoint = _target.AimPoint(HitboxType.Headshot);
        bool fired = _shooter.FireAt(headPoint);
        if (!fired)
        {
            Logger.Warn(
                "[ToyTricksDemo:Firefight] Native 'Shoot->Click' unavailable; no shot taken. " +
                $"Held item = {_shooter.Hub.inventory.CurItem.TypeId} — the trigger row only exists while a " +
                "firearm is EQUIPPED (an unequipped loadout weapon registers no Shoot listener).");
        }

        // Native fire is asynchronous (emulated trigger -> queued shot -> server backtrack + hitreg),
        // so the health delta is not readable on the firing frame.
        yield return Timing.WaitForSeconds(0.6f);
        if (!CanContinue())
        {
            yield break;
        }

        bool killed = !_target.IsAlive;
        float damage = healthBefore - Mathf.Max(0f, _target.Health);
        // Report where the shot actually landed rather than assuming: the aim asked for the head, but
        // only the observed hitbox proves it hit one. See OnPlayerHurt/OnPlayerDeath.
        string aftermath = _lastHitbox switch
        {
            HitboxType.Headshot => "HEADSHOT 爆头",
            null => "ONE SHOT 一击",
            _ => $"ONE SHOT 一击 ({_lastHitbox})",
        };
        string status = !fired ? "NO TRIGGER 无扳机" : killed ? aftermath : $"SURVIVED 未击杀 (-{damage:0})";
        SetStatus(status, ButtonIdle);
        Logger.Info(
            $"[ToyTricksDemo:Firefight] one shot: fired={fired} hitbox={_lastHitbox?.ToString() ?? "unobserved"} " +
            $"damage={damage:0.#} (target {healthBefore:0.#} HP) killed={killed}.");

        // Keep the aftermath (ragdoll on a moving platform) visible, then clean up so a re-press
        // starts from an empty stage.
        yield return Timing.WaitForSeconds(6f);
        if (!_destroyed)
        {
            ClearActors();
            SetStatus("READY 就绪 — press 按钮", ButtonIdle);
        }

        _running = false;
    }

    /// <summary>
    /// Resolves the configured shooter weapon. Returns <see cref="ItemType.None"/> to mean "keep the
    /// role's native loadout" (the AK). Non-automatic weapons are refused with a loud warning when the
    /// Harmony fixes are not applied, because unpatched they crash the server (revolver) or fire nothing
    /// at all (shotgun) — a config typo must not be able to reintroduce that.
    /// </summary>
    private static ItemType ResolveWeapon()
    {
        string configured = ToyTricksPlugin.Instance?.Config?.FirefightWeapon ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ItemType.None;
        }

        if (!Enum.TryParse(configured, ignoreCase: true, out ItemType weapon))
        {
            Logger.Warn(
                $"[ToyTricksDemo:Firefight] firefight_weapon '{configured}' is not a valid ItemType; " +
                "using the role's native loadout instead.");
            return ItemType.None;
        }

        bool needsPatches = weapon is ItemType.GunRevolver or ItemType.GunShotgun;
        if (needsPatches && !Patches.DummyFirearmPatches.IsApplied)
        {
            Logger.Warn(
                $"[ToyTricksDemo:Firefight] firefight_weapon '{weapon}' requires patch_dummy_firearms: true " +
                "(unpatched, the revolver NREs every frame and the shotgun never fires). " +
                "Falling back to the native loadout weapon.");
            return ItemType.None;
        }

        return weapon;
    }

    private bool CanContinue()
        => !_destroyed
            && _shooter != null
            && _target != null
            && !_shooterCarrier.IsDestroyed
            && !_targetCarrier.IsDestroyed;

    private void PlaceOnCarrier(DemoActor actor, WaypointToy carrier, float faceZ)
    {
        Transform deck = carrier.Transform;
        Vector3 world = deck.TransformPoint(PayloadLocalPosition);
        // Face along the lane axis so the two dummies start looking at each other.
        Quaternion facing = Quaternion.LookRotation(new Vector3(0f, 0f, faceZ), Vector3.up);
        actor.PlaceAt(world, deck.rotation * facing);
    }

    private static bool IsOnWaypoint(DemoActor? actor, AdminToyWaypoint expected)
        => actor != null && actor.IsOnWaypoint(expected);

    /// <summary>
    /// Both carriers on one coroutine: the near tram slides and yaws so the shooter has to re-aim, the
    /// far tram runs a longer, faster figure so the target crosses the firing lane. Two moving roots is
    /// the whole point of the bay, so their cycles are deliberately co-prime-ish (11 s vs 7.5 s) and
    /// never line up into a static relative pose.
    /// </summary>
    private IEnumerator<float> AnimateCarriers()
    {
        float startedAt = Time.timeSinceLevelLoad;
        while (!_destroyed && !_shooterCarrier.IsDestroyed && !_targetCarrier.IsDestroyed)
        {
            float now = Time.timeSinceLevelLoad - startedAt;

            float shooterPhase = (now / ShooterCycleSeconds) * Mathf.PI * 2f;
            _shooterCarrier.Position = _shooterAnchor + new Vector3(
                2.1f * Mathf.Sin(shooterPhase),
                0.12f * Mathf.Sin(shooterPhase * 2f),
                0.35f * Mathf.Cos(shooterPhase));
            _shooterCarrier.Rotation = Quaternion.Euler(0f, 10f * Mathf.Sin(shooterPhase), 0f);

            float targetPhase = (now / TargetCycleSeconds) * Mathf.PI * 2f;
            _targetCarrier.Position = _targetAnchor + new Vector3(
                3.4f * Mathf.Sin(targetPhase),
                0.18f * Mathf.Sin(targetPhase * 3f),
                0.9f * Mathf.Sin(targetPhase * 2f));
            _targetCarrier.Rotation = Quaternion.Euler(0f, 18f * Mathf.Cos(targetPhase), 0f);

            // Keep the shooter aimed at the HEAD while the scene is live, so it visibly tracks its
            // target across both carriers' motion instead of snapping only at the trigger pull. Stops
            // at the kill, leaving the shooter holding its final pose over the ragdoll.
            if (_running && _shooter != null && _target != null && _shooter.IsAlive && _target.IsAlive)
            {
                _shooter.LookAt(_target.AimPoint(HitboxType.Headshot));
            }

            yield return Timing.WaitForSeconds(AnimationStepSeconds);
        }
    }

    private void SetStatus(string text, Color capColor)
    {
        try
        {
            if (!_statusText.IsDestroyed && _statusText.Arguments.Count > 0)
            {
                // Arguments SyncList element delta — cheaper than re-sending the whole TextFormat.
                _statusText.Arguments[0] = text;
            }

            if (!_buttonCap.IsDestroyed)
            {
                _buttonCap.Color = capColor;
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"[ToyTricksDemo:Firefight] Status update failed: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Removes the dummies and the ragdolls their deaths created. Ragdolls are matched by owner hub
    /// BEFORE the hubs are destroyed (RagdollData.OwnerHub goes stale afterwards), so unrelated corpses
    /// on the map are never touched.
    /// </summary>
    private void ClearActors()
    {
        CollectOwnedRagdolls();
        DestroyOwnedRagdolls();

        _shooter?.Destroy();
        _shooter = null;
        _target?.Destroy();
        _target = null;
        _running = false;
    }

    private void CollectOwnedRagdolls()
    {
        ReferenceHub?[] owners = { _shooter?.Hub, _target?.Hub };
        foreach (BasicRagdoll ragdoll in RagdollManager.AllRagdolls)
        {
            if (ragdoll == null || ragdoll.gameObject == null)
            {
                continue;
            }

            if (owners.Any(owner => owner != null && ragdoll.Info.OwnerHub == owner))
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
                Logger.Warn($"[ToyTricksDemo:Firefight] Ragdoll cleanup failed: {exception.GetBaseException().Message}");
            }
        }

        _ownedRagdolls.Clear();
    }
}
