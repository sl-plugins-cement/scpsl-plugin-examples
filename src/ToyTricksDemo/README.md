# ToyTricksDemo

A standalone LabAPI plugin that spawns a reference gallery of the metarepo's in-game-verified
AdminToy tricks, calibrated to real-world meters, so future plugins (and agents) can copy
known-good code instead of rediscovering the traps:

1. **Replicated shear** — parallelograms, spikes, exact triangles, and fully 3D shards (3×3 SVD:
   a cube skewed on every axis at once) from the SVD parent/child rig.
2. **HDR bloom glow** — the translucent-HDR-albedo + LightSourceToy recipe, shown as an A/B/C/D ladder
   of sheared-diamond lances, plus an animated "strike array" slice (erupting/breathing/sinking lances
   driven by carrier-scale animation).
3. **World text** — `TextToy` with the meter calibration proven on the Crownfall DeCIRO console.
4. **Waypoint carrier** — a moving `WaypointToy` platform that carries an FPC dummy, a real pickup, and
   any player who steps on, through native relative positioning (nothing is Transform-parented).
5. **Moving firefight** — two independently moving carriers, an animated Chaos dummy shooting a
   Scientist dummy on the far one with real native ballistics, plus an `InteractableToy` play/reset
   button.
6. **Walking patrol** — dummies that genuinely *walk* a route through the native FPC motor while firing
   a **shotgun**, which is impossible on an unpatched server.

The plugin does nothing until the RA command is used, and cleans up after itself.

## Usage

| RA command | Effect |
| --- | --- |
| `toytricks` / `toytricks all` | Spawn all seven bays on the floor ~5.5 m in front of you, facing you |
| `toytricks shear` \| `glow` \| `strikealt` \| `text` \| `waypoint` \| `firefight` \| `patrol` | Spawn a single bay |
| `toytricks calibrate <factor>` | Set `TextUnitsToMeters` live (session only), then respawn to re-check |
| `toytricks clear` | Remove the gallery |
| alias | `ttd` |

Requires `FacilityManagement` or `ServerConfigs` RA permission and an alive in-game sender.
Only one gallery exists at a time; respawning replaces it. Round restarts destroy the toys with the
round; run `toytricks` again.

Config (`.../LabAPI/configs/<port>/ToyTricksDemo/config.yml`): `is_enabled`, `spawn_distance_meters`,
`text_units_to_meters` (persisted calibration factor), `patch_dummy_firearms` (server-wide Harmony fixes
for the revolver/shotgun dummy bugs — see below), `firefight_weapon`.

> **Stale-config trap:** an existing `config.yml` overrides new defaults, so adding these keys to the
> code does NOT make them appear on a port that already has a config file. Add them by hand (or delete
> the file and let it regenerate) or the new settings silently do nothing.

## The tricks (what to copy, and from where)

### 1. Replicated shear — `ShearMath.cs`, `ExhibitTools.Parallelogram/Triangle`

A single AdminToy replicates Position + Rotation + Scale = R·S: a rotated box, **never a shear**. But
`AdminToyBase` serializes the parent netId and clients rebuild the real transform hierarchy with LOCAL
transforms — so an invisible parent with **non-uniform scale** composed with a **rotated child** yields a
genuinely sheared world matrix on every client.

Any parallelogram spanned by edge vectors `(u, v)` factors by 2×2 SVD (`A = U·Σ·Vᵀ`, ported verbatim
from the ProjectMER fork's `TrianglePrimitiveBuilder`):

- parent (PrimitiveFlags.None): rotation `U` in the plane basis, scale `(σx, σy, 1)`
- child (Visible): rotation `−Vᵀ` about the plane normal; give the child z-scale for thickness
  (keep the parent z-scale at 1 so the thickness is not distorted).

Costs: parallelogram = 2 toys · spike (rhombus whose far corner `u+v` is the tip) = 2 toys ·
exact triangle = 3 medial parallelograms = 6 toys. Degenerate (collinear) edges are rejected.

**The 3D rig (`TryGetShearRig3D`, `ExhibitTools.Parallelepiped/Shard`):** the same idea with a real
3×3 SVD — THREE edge vectors span a parallelepiped (a cube skewed on every axis at once), factored
into parent rotation `U` + scale `(σ₁,σ₂,σ₃)` and child rotation `Vᵀ`, still 2 toys. The child keeps
unit scale (the singular values carry all sizing, so there is no separate thickness — span the third
edge for it). Left-handed edge triples are fixed by a column swap (a cube is mirror-symmetric);
coplanar/zero-volume triples are rejected. Numerically verified (8 shape cases + 500-case fuzz,
corner-to-corner error < 5e-7).

`Shard(base, axis, waistRadius, roll)` is the ready-made spike: three edges climbing to a single tip
at `base + axis`, splayed 120° apart around the axis — sharp at both ends from EVERY viewing angle,
where a flat sheared diamond vanishes edge-on. The shear bay's piece 5 shows it next to the flat spike;
the glow-bay strike lances and the strikealt bay's lance cores/thorn barbs are all Shards.

### 2. HDR bloom glow — `Exhibits/GlowExhibit.cs`

Primitives have **no reachable emission channel**: only `MaterialColor` is synced, and it is applied
client-side as `_BaseColor` (albedo) on an HDRP Lit material. The color is unclamped end-to-end, so HDR
values like `(12, 4, 7)` reach the client intact — but albedo alone never glows. The verified ladder:

| Pedestal | Setup | Result |
| --- | --- | --- |
| A | opaque LDR color, no light | flat control |
| B | `new Color(12,4,7, 0.6f)`, **no** light | stays dark — HDR albedo does NOT self-emit |
| C | same + co-located `LightSourceToy` | blooms white-hot — **the recipe** |
| D | C + opaque near-black slab behind | the glass rule (below) |

**The recipe:** alpha ≈ 0.6 (the transparent template reads as glowing glass, not dimmer) + very bright
unclamped HDR `_BaseColor` + a co-located `LightSourceToy` (the demo uses intensity 6, range 1.0 so
neighbors stay unlit; production walls used intensity ~7, range 3.5) + the game's HDRP bloom.

**The glass rule:** a translucent element's screen color ≈ tint × what is BEHIND it. Over the empty
void/skybox it is invisible no matter how much light hits it — every translucent glow element that can
face the void needs its own opaque near-black backdrop primitive.

### 2b. Strike array alt — `Exhibits/StrikeArrayAltExhibit.cs` (`toytricks strikealt`)

The denser 奇术打击阵列 recreation: nine lances in three depth waves — each a 3D Shard core plus two
crossed translucent diamond fins (6 toys), with thorn-barb Shards raked off the rear wave's lances.
Waves 1 and 2 punch through the opaque floor with back-eased overshoot and whole-wave tremors; only the
three wave ROOTS ever move (position/rotation at 30 Hz with per-frame client smoothing), all lance
geometry rides them statically. Light intensity also runs at 30 Hz. In
`all` mode it spawns one bay-depth behind the glow bay.

### 3. World text — `Exhibits/WorldTextExhibit.cs`, `TextAdvance.cs`

`TextToy` renders TMP text in world space; `TextFormat` (string), `DisplaySize` (Vector2, TMP units)
and the `Arguments` SyncList all replicate live even with `IsStatic = true`.

- **Meter calibration (the load-bearing number, IN-GAME MEASURED 2026-07-19):** rendered rect width in
  meters = `DisplaySize.x × uniformScale × 0.05`. Glyph height ≈ `TMP size × uniformScale × 0.05`.
  (The earlier 0.1 estimate from the DeCIRO console was 2× off.) The DisplaySize rect is INVISIBLE —
  TMP layout only — so the rulers pin `|` ink to the rect's left and right edges (per-line `<align>`
  rows collapsed with `<line-height=0>`) above a physical bar of the declared length: edge markers vs
  bar ends is the true comparison, confirmed lining up at 0.05. If a game update shifts it, re-tune with
  `toytricks calibrate <factor>` and persist `text_units_to_meters` in config.yml.
  `ExhibitTools.TextScaleFor(meters, units)` computes the scale for you.
- **Fitting lines:** `TextAdvance.Estimate/FitLine` is a conservative Liberation-Sans advance estimator
  (ASCII ≈ 0.59 em, CJK = 1 em, space 0.34, `·/:-` 0.48, bold ×1.03) in display units. Keep each
  `<nobr>` line under `DisplaySize.x − 10` (side bearing), e.g. 195 in a 205 box.
- **Cheap live updates:** put `{0}` in `TextFormat` and update `Arguments[0]` — a per-element SyncList
  delta instead of re-sending the whole format string. The gallery's uptime counter does this at 1 Hz.
- **Facing:** with a local identity rotation the text is readable by a viewer looking along the parent's
  +Z (the same convention as the native RA `toy spawn Text`).
- DisplaySize height barely matters for single-panel text (content taller than the box still renders);
  the width is what clips/wraps.
- **Glyph size is scale-driven:** the same physical width can be reached by ANY DisplaySize/scale pair,
  and the on-screen glyph height is `TMP size × scale × 0.1` meters. A small panel with a big
  DisplaySize (tiny scale) renders unreadably small letters — shrink DisplaySize.x and grow the scale
  instead. The gallery's rulers use DisplaySize 100 for exactly this reason.

### 4. Waypoint carrier — `Exhibits/WaypointExhibit.cs`

A `WaypointToy` is a native relative-positioning anchor: FPC movement modules and pickup physics pick
the nearest waypoint (squared distance minus `PriorityBias²`, within `BoundsSize`) and express their
synced positions relative to it. Move the waypoint and everything that selected it — dummies, pickups,
and REAL players standing on the platform — rides along smoothly, client-predicted, with zero plugin
code touching the passengers.

- Spawn the waypoint stationary; give payloads a beat to select it (the demo verifies association via
  `FpcModule.RelativePosition.WaypointId` / pickup transform parent before moving).
- `IsStatic` must be false; match `SyncInterval` to the animation cadence. Slow carriers use 15 Hz,
  while the glow and strike arrays require 30 Hz to avoid visible stepping in-game. The client's
  smoothing lerp runs per client frame, but it does not make their 10 Hz nonlinear motion look smooth.
  Animated light properties are not transform-smoothed and also remain at 30 Hz.
- **MovementSmoothing 256-invert trap:** the client lerp factor is `deltaTime × RAW × 0.3`, but the
  LabAPI wrapper property stores `256 − value`. Write `Base.NetworkMovementSmoothing = 60` (raw) for
  the buttery ride; the wrapper's `MovementSmoothing = 60` would write raw 196 ≈ instant snapping.
- Payload placement uses `Player.Position` = FPC capsule CENTRE (~1 m above feet): put a standing dummy
  at platform top + 1.0, not at deck level, or it spawns waist-deep.
- The bounds visualizer (`VisualizeBounds`) draws the red corner markers seen in screenshots — turn it
  off for production.

### 5. Moving firefight — `Exhibits/FirefightExhibit.cs`, `DemoActor.cs` (`toytricks firefight`)

Press the button on the near plinth: a Chaos Insurgency dummy on the near carrier tracks a Scientist
dummy riding the far carrier, settles its aim on the target's head, and drops it with **a single shot**
— both platforms moving on independent cycles the whole time. The shot is real — native trigger, native
hitreg, real ragdoll — so this bay is the reference for *animated dummies* rather than for geometry.

**Why one shot kills:** the target is set to **1 HP** (`DemoActor.SetHealth(1f)`), so any single hit is
lethal. That deliberately keeps the beat independent of per-weapon damage values and of whether the ray
happens to find the head — see the weapon-family constraint below for why picking a hard-hitting
one-shot weapon is not an option here.

The shot still *aims* for the head (`DemoActor.AimPoint(HitboxType.Headshot)` → the head hitbox's live
centre of mass), and the exhibit **reports where it actually landed** rather than assuming:
`HitboxIdentity.Damage` stamps `StandardDamageHandler.Hitbox` with the box the ray really struck, which
the bay reads back in `PlayerEvents.Hurt`/`Death`. Status shows `HEADSHOT 爆头` only when that field
really says `Headshot`; otherwise it says `ONE SHOT 一击 (<box>)`. For reference, the native multipliers
are ×2 Headshot and ×0.7 Limb (`FirearmDamageHandler.HitboxDamageMultipliers`).

Aim is settled for ~0.6 s **before** the trigger, not written in the same frame: `FpcMouseLook` applies
on the server's next rotation update and the shot's backtrack resolves against a slightly earlier
position, so a same-frame aim-and-fire turns a head hit into a body graze when both carriers are moving.

### Two native bugs stop dummies using non-automatic firearms (and how this plugin fixes them)

`Patches/DummyFirearmPatches.cs` carries Harmony fixes for two genuine bugs in the game's own firearm
code. Both are strict repairs of a path that, for a dummy, currently either **throws every frame** or
**does nothing at all**. Neither changes anything for a real player — their branches never reach the
patched code. Toggle with `patch_dummy_firearms` (default `true`); `firefight_weapon` selects the bay's
weapon and falls back to the loadout AK, with a warning, if you ask for a patched-only weapon with the
patches off.

**These are server-wide** — they affect every revolver and shotgun on the port, not just this plugin's
dummies. That is why they are a config toggle rather than always-on.

| # | Bug | Repair |
| --- | --- | --- |
| 1 | `DoubleActionModule.EquipUpdate` calls `Fire(null)` for anything `IsControllable`, but `FireLive` picks its backtrack with `IsLocalPlayer` — so a dummy reads from that null reader. `NullReferenceException` **once per frame, forever**. | Prefix on `FireLive`: when `extraData` is null, build the backtrack from the firearm via the public `ShotBacktrackData(Firearm)` ctor (exactly what the local-player branch uses), serialize it, and hand the original a reader over those bytes. Native code runs untouched. |
| 1b | Same flag mismatch a **third** time: `DoubleActionModule.Fire` calls `_triggerPull.Reset()` only on the `IsLocalPlayer` branch, so a dummy's pull never ends and `EquipUpdate` re-fires every `TimeBetweenShots` — one click becomes an endless stream. | Postfix on `Fire` (not `FireLive` — that is skipped on a dry chamber, which would loop just as badly): for dummies, perform the reset the local branch would have done. |
| 2 | `PumpActionModule.EquipUpdate` gates `UpdateClient()` — its **only** shot-enqueue path — on `IsLocalPlayer`, so a dummy's trigger reaches nothing. Silent no-op. | Postfix on `EquipUpdate`: for dummies only, when the native trigger is held, enqueue one firearm-derived `ShotBacktrackData` into the module's server queue, which `UpdateServer` drains through the ordinary `ProcessShot → hitreg` flow. |

All three are the same root cause: **`IsControllable` and `IsLocalPlayer` are used interchangeably in
code that predates dummies**, and a dummy is the one case where they differ.

Notes on the second patch, which is the less clean of the two: there is no public enqueue, so it uses
`AccessTools` on `_serverQueuedShots` and degrades to inert-with-a-warning if a game update renames that
field. It never fabricates chamber state — `ServerProcessShot`/`PullTrigger` still decide whether a round
exists — and the module's own `PumpIdle`/`AnyModuleBusy` gating remains the real rate limiter.

One subtlety worth copying: the synthesized reader's bytes are **copied out** of the pooled
`NetworkWriter` before it returns to the pool. A `NetworkReader` is a view over its buffer, so handing it
a pooled array that another caller may immediately overwrite is a use-after-free.

### Weapon families, and why the patches are needed

This cost a server-crashing NRE loop to learn, so it is worth stating flatly. The deciding line is each
action module's `EquipUpdate()` gate, and `AutosyncItem` defines
`IsControllable => IsLocalPlayer ? true : IsEmulatedDummy` — a dummy is **controllable but never local**.

| Module | Weapons | `EquipUpdate` gate | Unpatched | Patched |
| --- | --- | --- | --- | --- |
| `AutomaticActionModule` | AK, E-11, COM-15/18, Crossvec, FSP-9, Logicer | `IsControllable` → `ProcessInput`/`ProcessClientShots` → `SendCmd(ShotRequest)` | ✅ fires | ✅ (untouched) |
| `DoubleActionModule` | **Revolver** | `IsControllable` → `Fire(null)` | ❌ **NRE every frame** | ✅ patch #1 |
| `PumpActionModule` | **Shotgun** | `IsLocalPlayer` → `UpdateClient` | ❌ silent no-op | ✅ patch #2 |

- The **AK works** because its shot goes out through `SendCmd`, which on a dummy loops straight back into
  `ServerProcessCmd` carrying a real `ShotBacktrackData`.
- The **revolver hard-fails**: `DoubleActionModule.EquipUpdate` calls `Fire(null)` for any controllable
  player, and `FireLive` then does `new ShotBacktrackData(extraData)` on that null reader for anyone who
  is not `IsLocalPlayer`. Result is a `NullReferenceException` at
  `RelativePositioning.RelativePosition..ctor` **once per frame, forever** — observed in-game
  2026-07-25; it only stopped when the server was killed.
- The **shotgun fails silently**: `PumpActionModule` gates on `IsLocalPlayer`, and `UpdateClient` is the
  only thing that enqueues a shot. That file contains **zero** references to `IsControllable` or
  `IsEmulatedDummy`.

**The `Shoot->Click` row appearing in the RA Dummies panel proves nothing.** That row comes from
`SimpleTriggerModule` registering `ActionName.Shoot` as a listener, which happens for all three families
— the revolver's row is visibly present in the panel right before it NREs. Presence of the button and
ability to fire are independent facts.

**`DemoActor` is a demo-scale distillation of the batteries-included verbs in
`.tests\Playtest\Actors\{Actor,CombatFulfiller,MovementFulfiller}.cs`.** Same native paths, none of the
harness's fidelity/telemetry/monitor machinery (an exhibit must never fail a run). Copy from the harness
when you need assertions; copy from here when you need a scene.

The native paths, and why each one is the native path:

| Step | Native entry point |
| --- | --- |
| Spawn | `DummyUtils.SpawnDummy` — the exact RA `dummy` command path |
| Role | `hub.roleManager.ServerSetRole`, **one frame later** (see trap below) |
| Aim | `FpcMouseLook.CurrentHorizontal` (body yaw 0..360) / `CurrentVertical` (camera pitch, +up, ±88) |
| Aim point | the victim's live Body `HitboxIdentity.CenterOfMass` — the exact point hitreg tests |
| Equip | `Inventory.ServerSelectItem` — **required before firing**, see below |
| Reload | the `Reload->Click` dummy action → `AnimatorReloaderModuleBase` (real animation time) |
| Fire | the `Shoot->Click` dummy action from `DummyActionCollector.ServerGetActions` |

`DummyActionCollector.ServerGetActions` is the same registry the RA dummy panel renders, so invoking a
row is byte-identical to an admin clicking it: the whole server firearm pipeline runs (backtrack,
hitreg, `PlayerShootingWeapon` events, hitmarkers, damage, ragdoll).

Traps this bay encodes:

- **A dummy must EQUIP a firearm before it can shoot — giving it is not enough.** `Shoot->Click` is not
  a fixed row: `DummyKeyEmulator.PopulateDummyActions` emits one row per `ActionName` in its
  `_registeredListeners` list, and that list only grows when a module *calls* `GetAction(ActionName.Shoot, …)`.
  A firearm polls its input only while it is the held item, so an unequipped weapon (even a full native
  loadout with ammo) offers **no trigger row at all** and every shot silently no-ops. Symptom, observed
  in-game 2026-07-25: `Native 'Shoot->Click' unavailable; 0 native shot(s)`. Fix: `ServerSelectItem`,
  wait the draw, then poll until the row actually appears (`DemoActor.EquipFirearm` does all three) —
  the listener registers a frame or two AFTER the draw completes, so equipping and firing in the same
  frame still misses.
- **Deferred role set.** Setting the role on the SAME frame as `SpawnDummy` races
  `PlayerAuthenticationManager.Start`'s UserId assignment, and keycard loadout code
  (`SerialNumberDetail.GetNumberForPlayer`) NREs on the null UserId. Wait one frame.
- **Park the dummy first.** A fresh hub sits at the world origin until placed — teleport it far below
  the map on spawn or a body flashes in the middle of the facility.
- **Faction, not friendly fire.** `HitboxIdentity.IsDamageable` needs different *factions*.
  ChaosConscript (FoundationEnemy) vs Scientist (FoundationStaff) are enemies, so the shots land with
  server friendly fire OFF. Two MTF dummies would need FF on.
- **Aim per shot, not once.** The target's world position is driven by the *other* carrier; a cached aim
  point misses every time. The exhibit re-reads `AimPoint()` every shot and between shots (so it visibly
  tracks, instead of snapping only at the trigger pull).
- **Wait for waypoint binding.** `FirstPersonMovementModule` selects its waypoint on a later frame, not
  at placement. Until `FpcModule.RelativePosition.WaypointId` points at your carrier the dummy is
  standing in world space and the moving deck slides out from under it.
- **Never fabricate firearm state.** Readiness comes from the native reload action; the exhibit does not
  write `Cocked`/`BoltLocked`/`AmmoStored` (animation-internal state, not a player input).
- **Clear reserve ammo before destroying a dummy.** Native player teardown converts leftover reserve
  ammo into NEW world pickups that can no longer be traced to their owner.
- **Match ragdolls by owner BEFORE destroying the hubs.** `RagdollData.OwnerHub` goes stale afterwards,
  and a blind sweep would delete unrelated corpses.

### 6. Walking patrol — `Exhibits/PatrolExhibit.cs` (`toytricks patrol`)

Sits directly behind the firefight bay. Two Chaos dummies **walk** a rectangular route while engaging a
rotating cast of Scientist targets downrange with a **shotgun**; downed targets respawn so the loop runs
indefinitely. It demonstrates the two things the firefight bay cannot:

- **Native locomotion, no teleporting.** Each walker's
  `FpcMotor.ReceivedPosition` is set to its next route corner every tick, and the game's own motor
  steers there with real speed, acceleration, collision and step-up. That field is where a *real
  client's* movement lands, and setting it to an arbitrary world point is just the general form of the
  RA panel's `Walk forward 1.5m` dummy action (`FpcMotor.PopulateDummyActions`). Contrast
  `Player.Position = x`, which is a teleport.
- **A dummy firing a shotgun**, which only works because of patch #2 above — this bay is the live proof
  of that fix. With `patch_dummy_firearms: false` the patrol still walks but never shoots, and says so
  in the log and on its status board (`WALK ONLY 仅巡逻`).

Aim and locomotion are independent: the walkers track their nearest live target while continuing along
the route, so they shoot on the move. Targets are 1 HP so the loop stays brisk and does not depend on
buckshot pellet count or falloff at range.

### 7. The interaction button — `ExhibitTools.Button`

`InteractableToy` (native `InvisibleInteractableToy`) is an invisible collider that fires
`PlayerEvents.InteractedToy` when a player presses Interact while looking at it.

- **`InteractionDuration` MUST be 0.** Any positive value turns the toy into a HOLD-to-search
  (`ISearchable`) that raises `Searching`/`Searched` and **never** raises `InteractedToy`.
- Range is `StandardDistanceVerification`: 2.42 m client-side, ×1.4 server-side.
- The collider is invisible — always pair it with a visible primitive of the same footprint so players
  can see what they are pressing.
- **Debounce the handler.** The native interact route can deliver a press per client frame while the key
  is held; the firefight bay ignores presses within 0.75 s of the last one.

## Toy etiquette baked into `ExhibitTools`

- `networkSpawn: false` → set every property → `Spawn()`, so the first replication carries final state.
- `PrimitiveFlags`: `Visible = 2`, `Collidable = 1`, `None = 0`. Writing the raw int `1` makes an
  *invisible solid* — the classic trap. The gallery is Visible-only (walk-through).
- `IsStatic = true` on everything that never moves (TextToy syncvars still replicate).
- Every toy goes into one bag; teardown destroys in reverse spawn order with `IsDestroyed` guards.

## Known conflicts / limits

- **Harmony:** `patch_dummy_firearms` (default on) patches two native firearm bugs SERVER-WIDE — see the
  dummy-firearm section above. Set it to `false` for a Harmony-free install; the gallery works fine
  without it, the firefight bay just falls back to the AK.
- Events: the firefight bay subscribes `PlayerEvents.InteractedToy`, `Hurt`, and `Death` while spawned,
  each filtered strictly to its own toy/actor instances. No HSM. Everything else is pure AdminToys.
- The firefight bay creates two RA dummies while its scene is running. A role-stomping plugin on the
  same port (or a `Team.ChaosInsurgency` count that flips round-end conditions) can interfere with it —
  it is a developer tool, not something to leave running on a live round.
- The glow bay needs a normally-lit or dark room to read correctly; pedestal B may still pick up bright
  ambient room light (that incident light IS the effect being demonstrated).
- Captions are English-with-Chinese-keywords by design (this is a developer tool, not player-facing UI).
