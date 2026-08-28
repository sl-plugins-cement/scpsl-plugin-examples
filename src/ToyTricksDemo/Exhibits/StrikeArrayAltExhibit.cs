using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using UnityEngine;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// Alternative 奇术打击阵列 slice: nine sheared lances move from a calm three-spire silhouette into a
/// dense three-wave volley. Each lance is now spiked on EVERY axis: a 3D-shard core (the 3x3-SVD
/// parallelepiped rig — sharp from any viewing angle) wrapped by two perpendicular SVD diamond fins
/// whose overlap becomes a white-hot core under HDR bloom, and the rear wave grows thorn barbs — small
/// shards raked off the lance axis — so the final wall reads jagged instead of picket-fence. Only three
/// wave roots move; all lance/pool geometry toys stay static (6 lance toys + 1 pool each, +4 per barbed
/// rear lance).
/// </summary>
internal sealed class StrikeArrayAltExhibit
{
    private const int WaveCount = 3;
    private const float AnimationStepSeconds = 1f / 30f;
    private const float CycleSeconds = 7f;
    private const float FloorSurfaceY = 0.075f;

    private static readonly Color FloorColor = new(0.025f, 0.015f, 0.045f, 1f);
    private static readonly Color BackdropColor = new(0.018f, 0.008f, 0.032f, 1f);
    private static readonly Color LanceGlass = new(16f, 7f, 23f, 0.62f);
    private static readonly Color PoolGlass = new(6f, 1.8f, 11f, 0.48f);

    // The three depth layers deliberately leave negative space in the idle wave, then interlock when the
    // two buried waves erupt. Heights and lean vectors avoid the mechanical "equal fence post" silhouette.
    private static readonly LanceSpec[] LanceLayout =
    {
        // Wave 0: sparse sentinels, visible throughout the calm portion of the cycle.
        new(0, -1.38f, 0.48f, 2.18f, 0.18f, 0.14f, 0.25f, 14f),
        new(0,  0.02f, 0.28f, 3.18f, 0.08f, -0.12f, 0.30f, -9f),
        new(0,  1.48f, 0.72f, 1.92f, -0.16f, 0.10f, 0.23f, 24f),

        // Wave 1: front/middle gap-fillers; these make the first violent broadside.
        new(1, -1.88f, 0.86f, 1.62f, 0.13f, -0.08f, 0.20f, -18f),
        new(1, -0.72f, 0.04f, 2.78f, -0.20f, 0.16f, 0.27f, 31f),
        new(1,  0.78f, 0.10f, 2.52f, 0.19f, 0.08f, 0.26f, -26f),

        // Wave 2: rear needles arrive last and turn the composition into the dense wall-of-blades beat.
        new(2, -0.30f, 0.94f, 2.16f, 0.14f, -0.13f, 0.22f, 8f),
        new(2,  1.06f, 1.00f, 2.96f, -0.18f, -0.17f, 0.28f, 37f),
        new(2,  1.90f, 0.56f, 1.70f, -0.12f, 0.13f, 0.20f, -35f),
    };

    private static readonly float[] HideDepths = { 0f, 3.25f, 3.45f };
    private static readonly Vector3[] LightPositions =
    {
        new(0.02f, 1.45f, 0.38f),
        new(-0.55f, 1.25f, 0.34f),
        new(0.78f, 1.35f, 0.82f),
    };

    private static readonly Color[] LightColors =
    {
        new(0.84f, 0.62f, 1f, 1f),
        new(0.72f, 0.42f, 1f, 1f),
        new(0.58f, 0.34f, 1f, 1f),
    };

    private readonly PrimitiveObjectToy[] _waveRoots = new PrimitiveObjectToy[WaveCount];
    private readonly LightSourceToy[] _waveLights = new LightSourceToy[WaveCount];
    private readonly Vector3[] _waveAnchors = new Vector3[WaveCount];

    private CoroutineHandle _routine;
    private bool _stopped;

    private StrikeArrayAltExhibit()
    {
    }

    public static StrikeArrayAltExhibit Build(Transform root, Vector3 bayOffset, List<AdminToy> bag)
    {
        StrikeArrayAltExhibit exhibit = new();
        PrimitiveObjectToy bay = ExhibitTools.InvisibleParent(
            bayOffset, Quaternion.identity, Vector3.one, root, bag);
        Transform bayTransform = bay.Transform;

        // Opaque near-black surfaces are functional, not decoration: translucent HDR primitives lose their
        // body over the void, while this floor/back wall gives both sharp silhouette and a bloom-readable base.
        ExhibitTools.Visible(
            new Vector3(0f, 0f, 0.30f),
            Quaternion.identity,
            new Vector3(4.75f, 0.15f, 2.55f),
            FloorColor,
            bayTransform,
            bag);
        ExhibitTools.Visible(
            new Vector3(0f, 1.82f, 1.48f),
            Quaternion.identity,
            new Vector3(4.75f, 3.65f, 0.06f),
            BackdropColor,
            bayTransform,
            bag);
        ExhibitTools.Text(
            new Vector3(0f, 3.78f, 1.43f),
            2.25f,
            new Vector2(260f, 24f),
            "<align=center><size=12><b>STRIKE ARRAY ALT  奇术打击阵列</b></size>\n<size=8>crossed SVD needles · sparse calm → dense volley</size></align>",
            bayTransform,
            bag);

        for (int wave = 0; wave < WaveCount; wave++)
        {
            Vector3 anchor = new(0f, FloorSurfaceY, 0f);
            PrimitiveObjectToy waveRoot = ExhibitTools.InvisibleParent(
                anchor,
                Quaternion.identity,
                Vector3.one,
                bayTransform,
                bag,
                isStatic: false);

            exhibit._waveAnchors[wave] = anchor;
            exhibit._waveRoots[wave] = waveRoot;
        }

        for (int index = 0; index < LanceLayout.Length; index++)
        {
            LanceSpec spec = LanceLayout[index];
            Transform waveTransform = exhibit._waveRoots[spec.Wave].Transform;
            Vector3 basePosition = new(spec.X, 0f, spec.Z);

            // Shard core + two perpendicular sheared diamond fins (6 toys): the core is skewed on every
            // axis at once and never vanishes edge-on, while the crossed translucent fins overlap into a
            // bright axial streak under HDR bloom.
            SpikedLance(
                basePosition,
                spec.Width,
                spec.Height,
                spec.LeanX,
                spec.LeanZ,
                spec.Yaw,
                LanceGlass,
                waveTransform,
                bag);

            // Thorn barbs on the rear wave only: short shards raked ~40° off the lance axis, one per
            // side, so the densest beat of the volley reads jagged rather than picket-fence. They ride
            // the same wave root, so eruption/tremor animation costs nothing extra.
            if (spec.Wave == 2)
            {
                Vector3 lanceAxis = new(spec.LeanX, spec.Height, spec.LeanZ);
                Vector3 barbRoot = basePosition + (lanceAxis * 0.45f);
                Vector3 side = Quaternion.AngleAxis(spec.Yaw, Vector3.up) * Vector3.right;
                float barbLength = spec.Height * 0.3f;
                ExhibitTools.Shard(
                    barbRoot,
                    ((side * 0.75f) + (lanceAxis.normalized * 0.85f)).normalized * barbLength,
                    spec.Width * 0.28f, 0f, LanceGlass, waveTransform, bag);
                ExhibitTools.Shard(
                    barbRoot + (lanceAxis * 0.13f),
                    ((side * -0.7f) + (lanceAxis.normalized * 0.9f)).normalized * (barbLength * 0.85f),
                    spec.Width * 0.24f, 60f, LanceGlass, waveTransform, bag);
            }

            // One very flat HDR cylinder reads as a floor scorch/glow pool. It rides the wave root, so it is
            // naturally hidden beneath the opaque floor until its associated eruption reaches the surface.
            ExhibitTools.Visible(
                basePosition + new Vector3(0f, 0.012f, 0f),
                Quaternion.identity,
                new Vector3(spec.Width * 2.5f, 0.024f, spec.Width * 1.75f),
                PoolGlass,
                waveTransform,
                bag,
                PrimitiveType.Cylinder);
        }

        for (int wave = 0; wave < WaveCount; wave++)
        {
            exhibit._waveRoots[wave].SyncInterval = AnimationStepSeconds;
            exhibit._waveLights[wave] = ExhibitTools.Glow(
                LightPositions[wave],
                LightColors[wave],
                0f,
                2.75f,
                exhibit._waveRoots[wave].Transform,
                bag,
                isStatic: false);
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
    /// Three carrier roots do all transform work. Wave 0 holds the sparse reference pose; waves 1 and 2
    /// punch through the floor with back-eased overshoot, overlap as a dense wall, then withdraw in reverse.
    /// Tiny whole-wave tremors during the overlap prevent the climax from reading as a frozen picket fence.
    /// </summary>
    private IEnumerator<float> AnimateArray()
    {
        while (!_stopped)
        {
            float now = Time.timeSinceLevelLoad;
            float cycle = Mathf.Repeat(now / CycleSeconds, 1f);

            float waveOne = VolleyAmount(cycle, 0.22f, 0.36f, 0.74f, 0.88f);
            float waveTwo = VolleyAmount(cycle, 0.31f, 0.44f, 0.65f, 0.80f);
            float density = Mathf.Clamp01(Mathf.Max(waveOne, waveTwo));

            UpdateWave(0, 1f, density, now, 0.0f);
            UpdateWave(1, waveOne, density, now, 1.7f);
            UpdateWave(2, waveTwo, density, now, 3.4f);

            yield return Timing.WaitForSeconds(AnimationStepSeconds);
        }
    }

    private void UpdateWave(int wave, float amount, float density, float now, float phase)
    {
        PrimitiveObjectToy root = _waveRoots[wave];
        LightSourceToy light = _waveLights[wave];
        if (root.IsDestroyed || light.IsDestroyed)
        {
            return;
        }

        float visible = Mathf.Clamp01(amount);
        float agitation = visible * density;
        Vector3 anchor = _waveAnchors[wave];

        // Positioning the intact rig through an opaque floor reveals the sharp top tip first. This preserves
        // each lance's designed lean and waist, unlike anisotropic y-scaling which bends tilted needles flat.
        float xTremor = 0.018f * agitation * Mathf.Sin((now * 13f) + phase);
        float yTremor = 0.026f * visible * Mathf.Sin((now * 7f) + (phase * 0.7f));
        float zTremor = 0.012f * agitation * Mathf.Sin((now * 11f) + (phase * 1.3f));
        root.Position = anchor + new Vector3(
            xTremor,
            (-((1f - amount) * HideDepths[wave])) + yTremor,
            zTremor);
        root.Rotation = Quaternion.Euler(
            0.35f * agitation * Mathf.Sin((now * 8f) + phase),
            1.8f * agitation * Mathf.Sin((now * 5.5f) + phase),
            0.45f * agitation * Mathf.Sin((now * 9f) + (phase * 0.5f)));

        // Light properties do not use transform smoothing, so preserve the full 30 Hz pulse.
        float idleLight = wave == 0 ? 3.8f : 0f;
        float strikeLight = visible * (7.2f + (3.6f * density));
        float shimmer = visible * 0.8f * (0.5f + (0.5f * Mathf.Sin((now * 10f) + phase)));
        light.Intensity = Mathf.Max(idleLight, strikeLight) + shimmer;
    }

    /// <summary>
    /// A six-toy spiked needle: a 3D-shard core (3x3-SVD parallelepiped, sharp from every angle) plus
    /// two crossed SVD diamond planes (each an invisible parent + cube child), the second rotated 90
    /// degrees around the lance axis' vertical projection — their translucent overlap is the hot streak.
    /// </summary>
    private static void SpikedLance(
        Vector3 basePosition,
        float width,
        float height,
        float leanX,
        float leanZ,
        float yawDegrees,
        Color color,
        Transform parent,
        List<AdminToy> bag)
    {
        Vector3 axis = new(leanX, height, leanZ);
        Quaternion yaw = Quaternion.AngleAxis(yawDegrees, Vector3.up);
        Vector3 sideA = yaw * (Vector3.right * (width * 0.5f));
        Vector3 sideB = yaw * (Vector3.forward * (width * 0.5f));
        Vector3 halfAxis = axis * 0.5f;
        float thickness = Mathf.Max(width * 0.19f, 0.045f);

        // The solid core: sharp tips at BOTH ends from any viewing angle. Slightly slimmer than the
        // fins so the translucent planes still read as blades around a bright spine.
        ExhibitTools.Shard(basePosition, axis, width * 0.34f, yawDegrees, color, parent, bag);

        ExhibitTools.Parallelogram(
            basePosition,
            halfAxis + sideA,
            halfAxis - sideA,
            thickness,
            color,
            parent,
            bag);
        ExhibitTools.Parallelogram(
            basePosition,
            halfAxis + sideB,
            halfAxis - sideB,
            thickness,
            color,
            parent,
            bag);
    }

    private static float VolleyAmount(float cycle, float riseStart, float riseEnd, float fallStart, float fallEnd)
    {
        if (cycle < riseStart || cycle >= fallEnd)
        {
            return 0f;
        }

        if (cycle < riseEnd)
        {
            return EaseOutBack((cycle - riseStart) / (riseEnd - riseStart));
        }

        if (cycle < fallStart)
        {
            return 1f;
        }

        return 1f - SmoothStep((cycle - fallStart) / (fallEnd - fallStart));
    }

    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t) - 1f;
        const float Overshoot = 1.22f;
        return 1f + ((Overshoot + 1f) * t * t * t) + (Overshoot * t * t);
    }

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - (2f * t));
    }

    private readonly struct LanceSpec
    {
        public LanceSpec(
            int wave,
            float x,
            float z,
            float height,
            float leanX,
            float leanZ,
            float width,
            float yaw)
        {
            Wave = wave;
            X = x;
            Z = z;
            Height = height;
            LeanX = leanX;
            LeanZ = leanZ;
            Width = width;
            Yaw = yaw;
        }

        public int Wave { get; }
        public float X { get; }
        public float Z { get; }
        public float Height { get; }
        public float LeanX { get; }
        public float LeanZ { get; }
        public float Width { get; }
        public float Yaw { get; }
    }
}
