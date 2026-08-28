using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using UnityEngine;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// Bay 2 — the HDR "glow" recipe (in-game verified on the Serpent's Hand spiky wall + Way portal):
/// every lance is a pure SHEARED DIAMOND — one parallelogram rig whose bottom AND top corners are sharp
/// tips (2 toys, no column) — yawed so the shear plane is genuinely 3D. The A/B/C/D pedestal ladder:
///   A. opaque LDR, no light                 -> flat control,
///   B. translucent HDR, NO light            -> stays dark: primitives have NO reachable emission,
///   C. translucent HDR + co-located light   -> blooms white-hot: THE recipe
///      (alpha ≈ 0.6 + unclamped HDR color + LightSourceToy + the game's HDRP bloom),
///   D. recipe C + an opaque near-black backdrop -> the glass rule (translucent glow over the void is
///      invisible; it needs an opaque backdrop).
/// Behind the ladder, the STRIKE ARRAY: a slice of the 奇术打击阵列 effect — a staggered row of
/// white-violet lances that erupt from the floor, breathe, and sink, animated by scaling each lance's
/// own non-static carrier root (the client lerps scale like position, so growth is smooth). The lances
/// are 3D SHARDS (the 3x3-SVD rig, `ExhibitTools.Shard`): unlike the ladder's flat diamonds they stay
/// needle-sharp from every viewing angle instead of vanishing edge-on — still 2 toys each.
/// </summary>
internal sealed class GlowExhibit
{
    private const float AnimationStepSeconds = 1f / 30f;
    private const float ArrayCycleSeconds = 5f;

    private static readonly Color OpaqueControl = new(0.55f, 0.08f, 0.2f, 1f);
    private static readonly Color HdrGlass = new(12f, 4f, 7f, 0.6f);        // the verified recipe color
    private static readonly Color GlowLight = new(1f, 0.45f, 0.6f, 1f);
    private static readonly Color ArrayGlass = new(14f, 6f, 18f, 0.6f);     // white-violet lance color
    private static readonly Color ArrayLight = new(0.75f, 0.5f, 1f, 1f);
    private static readonly Color BackdropColor = new(0.06f, 0.02f, 0.04f, 1f);
    private static readonly Color PlinthColor = new(0.09f, 0.1f, 0.12f, 1f);

    // Strike-array slice: (x, z, lance height, phase offset in cycles) — staggered like the reference.
    private static readonly (float X, float Z, float Height, float Phase)[] ArrayLayout =
    {
        (-1.7f, 0.9f, 1.9f, 0.00f),
        (-0.85f, 1.1f, 2.4f, 0.20f),
        (0f, 0.95f, 2.9f, 0.40f),
        (0.85f, 1.1f, 2.4f, 0.60f),
        (1.7f, 0.9f, 1.9f, 0.80f),
    };

    private readonly List<(PrimitiveObjectToy Carrier, LightSourceToy Light, float Phase)> _lances = new();
    private CoroutineHandle _routine;
    private bool _stopped;

    private GlowExhibit()
    {
    }

    public static GlowExhibit Build(Transform root, Vector3 bayOffset, List<AdminToy> bag)
    {
        GlowExhibit exhibit = new();
        PrimitiveObjectToy bay = ExhibitTools.InvisibleParent(bayOffset, Quaternion.identity, Vector3.one, root, bag);
        Transform t = bay.Transform;

        ExhibitTools.Visible(new Vector3(0f, 0.025f, 0.35f), Quaternion.identity, new Vector3(4.2f, 0.05f, 1.7f), PlinthColor, t, bag);
        ExhibitTools.Text(new Vector3(0f, 2.05f, -0.4f), 1.9f, new Vector2(200f, 22f),
            "<align=center><size=13><b>HDR GLOW 泛光</b></size>\n<size=8>alpha 0.6 + HDR albedo + LightSourceToy + bloom</size></align>", t, bag);

        // A/B/C/D pedestal ladder — every shard is one sheared diamond (2 toys), yawed for 3D shear.
        Diamond(t, -1.65f, OpaqueControl, 25f, bag);
        Caption(t, -1.65f, "A. LDR, no light\n对照：不发光", bag);

        Diamond(t, -0.55f, HdrGlass, -30f, bag);
        Caption(t, -0.55f, "B. HDR, no light\n无灯不自发光", bag);

        Diamond(t, 0.55f, HdrGlass, 35f, bag);
        // Co-located light on the viewer side; short range so it cannot touch the neighbors' A/B result.
        ExhibitTools.Glow(new Vector3(0.55f, 0.75f, -0.4f), GlowLight, 6f, 1.0f, t, bag);
        Caption(t, 0.55f, "C. HDR + light\n白热泛光", bag);

        Diamond(t, 1.65f, HdrGlass, -25f, bag);
        ExhibitTools.Glow(new Vector3(1.65f, 0.75f, -0.4f), GlowLight, 6f, 1.0f, t, bag);
        // The opaque near-black slab BEHIND the glass: without one, recipe C vanishes against the void.
        ExhibitTools.Visible(new Vector3(1.65f, 0.85f, 0.3f), Quaternion.identity, new Vector3(0.9f, 1.8f, 0.03f), BackdropColor, t, bag);
        Caption(t, 1.65f, "D. + backdrop\n玻璃需要背景", bag);

        // Strike-array slice on the back half of the plinth.
        ExhibitTools.Text(new Vector3(0f, 2.45f, 1.0f), 1.6f, new Vector2(200f, 20f),
            "<align=center><size=11><b>STRIKE ARRAY 打击阵列</b></size>\n<size=8>animated carrier scale + light intensity</size></align>", t, bag);
        foreach ((float x, float z, float height, float phase) in ArrayLayout)
        {
            // Non-static carrier root per lance: animating ITS local scale grows/sinks the whole shear
            // rig underneath, and the client's transform lerp keeps the eruption smooth.
            PrimitiveObjectToy carrier = ExhibitTools.InvisibleParent(
                new Vector3(x, 0f, z), Quaternion.identity, Vector3.one, t, bag, isStatic: false);
            carrier.SyncInterval = AnimationStepSeconds;
            // 3D shard, not a flat diamond: sharp from every angle. Keep the axis lean small — the
            // carrier's y-scale eruption scales only the vertical component, so a big lean would
            // visibly bend during the grow phase (a small one just reads as organic straightening).
            ExhibitTools.Shard(
                Vector3.zero, new Vector3(x * 0.04f, height, 0.03f), 0.14f, x * 23f, ArrayGlass, carrier.Transform, bag);
            LightSourceToy light = ExhibitTools.Glow(
                new Vector3(0f, height * 0.5f, -0.35f), ArrayLight, 0f, 2.2f, carrier.Transform, bag, isStatic: false);
            exhibit._lances.Add((carrier, light, phase));
        }

        return exhibit;
    }

    public void Start()
    {
        if (!_stopped && !_routine.IsRunning)
        {
            _routine = Timing.RunCoroutine(AnimateArray());
        }
    }

    public void Stop()
    {
        _stopped = true;
        Timing.KillCoroutines(_routine);
    }

    /// <summary>
    /// The eruption loop: each lance runs the same cycle offset by its phase — thrust up from the floor
    /// (ease-out), breathe at full height, sink away, rest. Carrier y-scale carries the motion (the base
    /// sits at y=0, so scaling pushes the tip up while the bottom point stays planted in the floor) and
    /// the light intensity tracks the visible height so the bloom pulses with the lance.
    /// </summary>
    private IEnumerator<float> AnimateArray()
    {
        while (!_stopped)
        {
            float now = Time.timeSinceLevelLoad;
            foreach ((PrimitiveObjectToy carrier, LightSourceToy light, float phase) in _lances)
            {
                if (carrier.IsDestroyed || light.IsDestroyed)
                {
                    continue;
                }

                float cycle = Mathf.Repeat((now / ArrayCycleSeconds) + phase, 1f);
                float presence = cycle switch
                {
                    < 0.18f => EaseOut(cycle / 0.18f),                        // erupt
                    < 0.55f => 1f + (0.06f * Mathf.Sin((cycle - 0.18f) * 40f)), // breathe
                    < 0.75f => 1f - EaseOut((cycle - 0.55f) / 0.2f),          // sink
                    _ => 0f,                                                   // rest
                };

                // Never scale fully to 0 (degenerate matrices); park the lance just under the floor.
                float y = Mathf.Max(presence, 0.02f);
                carrier.Scale = new Vector3(1f, y, 1f);

                // Light properties are not covered by AdminToy transform smoothing, so they share the
                // full 30 Hz cadence with the carrier scale.
                light.Intensity = 9f * Mathf.Clamp01(presence);
            }

            yield return Timing.WaitForSeconds(AnimationStepSeconds);
        }
    }

    private static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - ((1f - t) * (1f - t));
    }

    /// <summary>A pedestal shard: ONE sheared diamond, both tips sharp, yawed so the shear is 3D.</summary>
    private static void Diamond(Transform bay, float x, Color color, float yawDegrees, List<AdminToy> bag)
    {
        ExhibitTools.Diamond(new Vector3(x, 0.05f, 0f), 0.42f, 1.5f, 0.2f, yawDegrees, color, bay, bag);
    }

    private static void Caption(Transform bay, float x, string text, List<AdminToy> bag)
    {
        // 0.9 m wide: pedestals are 1.1 m apart, so neighboring captions keep clear air.
        ExhibitTools.Text(new Vector3(x, 0.16f, -0.45f), 0.9f, new Vector2(100f, 34f),
            $"<align=center><size=11>{text}</size></align>", bay, bag);
    }
}
