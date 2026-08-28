using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace ToyTricksDemo;

/// <summary>
/// Shared spawn helpers for the exhibits. Every helper follows the metarepo's proven toy etiquette:
/// networkSpawn:false -> set all properties -> Spawn() (so the first replication already carries the
/// final state), PrimitiveFlags.Visible (=2; the raw int 1 is Collidable = an INVISIBLE solid — a
/// classic trap), IsStatic=true for anything that never moves, and every toy tracked in a bag so the
/// gallery can be torn down with one call.
/// </summary>
internal static class ExhibitTools
{
    /// <summary>
    /// TextToy world-width calibration: the rendered DisplaySize rect width in METERS is
    /// DisplaySize.x * uniformScale * this factor.
    /// IN-GAME MEASURED 2026-07-19 with the text bay's edge-marker rulers (markers pinned to the rect
    /// edges over an exactly-1.00 m bar): the true factor is 0.05. The earlier 0.1 came from an
    /// eyeballed fit of the Crownfall DeCIRO console and was 2x off — meaning that console's panel is
    /// physically ~0.82 m, not the ~1.64 m its comment claims. Tunable live via
    /// `toytricks calibrate` / the text_units_to_meters config if a game update shifts it.
    /// Glyph height in meters ≈ TMP size × uniformScale × this same factor.
    /// </summary>
    public static float TextUnitsToMeters = 0.05f;

    /// <summary>Uniform TextToy scale that renders <paramref name="displayUnitsWide"/> TMP units as <paramref name="metersWide"/> meters.</summary>
    public static float TextScaleFor(float metersWide, float displayUnitsWide)
        => metersWide / (displayUnitsWide * TextUnitsToMeters);

    /// <summary>
    /// The RAW MovementSmoothing for animated toys. The client lerp factor is deltaTime × RAW × 0.3 and
    /// the LabAPI wrapper property stores 256−value, so always write this to Base.NetworkMovementSmoothing
    /// (raw 60 ≈ buttery; wrapper 60 would be raw 196 ≈ instant snap).
    /// </summary>
    public const byte RawMovementSmoothing = 60;

    /// <summary>
    /// An invisible, non-colliding primitive used as a rig root or shear parent. Pass isStatic:false for
    /// roots that will be animated (position/rotation/scale changes replicate and smooth client-side).
    /// </summary>
    public static PrimitiveObjectToy InvisibleParent(
        Vector3 position, Quaternion rotation, Vector3 scale, Transform? parent, List<AdminToy> bag,
        bool isStatic = true)
    {
        PrimitiveObjectToy toy = PrimitiveObjectToy.Create(position, rotation, scale, parent, networkSpawn: false);
        toy.Type = PrimitiveType.Cube;
        toy.Flags = PrimitiveFlags.None;
        toy.Color = new Color(0f, 0f, 0f, 0f);
        toy.IsStatic = isStatic;
        if (!isStatic)
        {
            toy.Base.NetworkMovementSmoothing = RawMovementSmoothing;
            toy.SyncInterval = 0f;
        }

        toy.Spawn();
        bag.Add(toy);
        return toy;
    }

    /// <summary>A visible primitive (cube unless overridden; non-colliding unless flags say otherwise).</summary>
    public static PrimitiveObjectToy Visible(
        Vector3 localPos, Quaternion localRot, Vector3 size, Color color, Transform parent, List<AdminToy> bag,
        PrimitiveType type = PrimitiveType.Cube, PrimitiveFlags flags = PrimitiveFlags.Visible)
    {
        PrimitiveObjectToy toy = PrimitiveObjectToy.Create(localPos, localRot, size, parent, networkSpawn: false);
        toy.Type = type;
        toy.Flags = flags;
        toy.Color = color;
        toy.IsStatic = true;
        toy.Spawn();
        bag.Add(toy);
        return toy;
    }

    /// <summary>
    /// A point light (the only real "emitter" available to plugins — primitives cannot self-emit).
    /// Pass isStatic:false when the intensity/color will be animated (syncvar changes need it).
    /// </summary>
    public static LightSourceToy Glow(
        Vector3 localPos, Color color, float intensity, float range, Transform parent, List<AdminToy> bag,
        bool isStatic = true)
    {
        LightSourceToy light = LightSourceToy.Create(localPos, Quaternion.identity, parent, networkSpawn: false);
        light.Type = LightType.Point;
        light.Color = color;
        light.Intensity = intensity;
        light.Range = range;
        light.ShadowType = LightShadows.None;
        light.IsStatic = isStatic;
        if (!isStatic)
        {
            light.SyncInterval = 0f;
        }

        light.Spawn();
        bag.Add(light);
        return light;
    }

    /// <summary>
    /// An <see cref="InteractableToy"/> press-button: an invisible box collider that fires
    /// PlayerEvents.InteractedToy the instant a player presses their Interact key while looking at it.
    /// InteractionDuration MUST stay 0 — any positive value turns the toy into a HOLD-to-search
    /// (ISearchable) instead, which raises Searching/Searched and never raises InteractedToy.
    /// The collider lives on the toy's own GameObject and is sized by <paramref name="size"/>; pair it
    /// with a visible primitive of the same footprint so players can see what they are pressing.
    /// </summary>
    public static InteractableToy Button(
        Vector3 localPos, Quaternion localRot, Vector3 size, Transform parent, List<AdminToy> bag)
    {
        InteractableToy button = InteractableToy.Create(localPos, localRot, size, parent, networkSpawn: false);
        button.Shape = AdminToys.InvisibleInteractableToy.ColliderShape.Box;
        button.InteractionDuration = 0f;
        button.IsLocked = false;
        button.IsStatic = true;
        button.Spawn();
        bag.Add(button);
        return button;
    }

    /// <summary>
    /// A world text panel calibrated to a real-world width. Local identity rotation faces the same way
    /// the native RA "toy spawn Text" convention does: readable by a viewer looking along the parent's +Z.
    /// </summary>
    public static TextToy Text(
        Vector3 localPos, float metersWide, Vector2 displaySize, string tmpText, Transform parent, List<AdminToy> bag)
    {
        float scale = TextScaleFor(metersWide, displaySize.x);
        TextToy text = TextToy.Create(localPos, Quaternion.identity, Vector3.one * scale, parent, networkSpawn: false);
        text.DisplaySize = displaySize;
        text.TextFormat = tmpText;
        text.IsStatic = true;
        text.Spawn();
        bag.Add(text);
        return text;
    }

    /// <summary>
    /// One sheared parallelogram: origin corner + two edge vectors (all in the parent's local frame),
    /// realized as an invisible SVD-rotated/scaled parent plus a rotated child cube of the given
    /// thickness. 2 toys. Returns false (spawning nothing) for degenerate edges.
    /// </summary>
    public static bool Parallelogram(
        Vector3 origin, Vector3 edgeX, Vector3 edgeY, float thickness, Color color, Transform parent, List<AdminToy> bag)
    {
        if (!ShearMath.TryGetShearRig(edgeX, edgeY, out Vector3 center, out Quaternion parentRot, out Vector3 parentScale, out Quaternion childRot))
        {
            return false;
        }

        PrimitiveObjectToy shearParent = InvisibleParent(origin + center, parentRot, parentScale, parent, bag);
        // The child's z-axis is the rig's rotation axis, so a plain z thickness survives the shear.
        // NOTE the child z size is divided by the parent's z scale (=1 here) on clients; keep parent z at 1.
        Visible(Vector3.zero, childRot, new Vector3(1f, 1f, thickness), color, shearParent.Transform, bag);
        return true;
    }

    /// <summary>
    /// A vertical DIAMOND (rhombus) lance: ONE sheared parallelogram whose bottom corner is
    /// <paramref name="baseLocal"/> and whose far corner (u+v) is the top tip — sharp points at both
    /// ends, 2 toys. <paramref name="yawDegrees"/> spins the blade plane about the vertical axis, making
    /// the edge vectors genuinely 3D (the SVD rig factors ANY 3D parallelogram, not just axis-aligned
    /// planes). Waist width is at half height.
    /// </summary>
    public static void Diamond(
        Vector3 baseLocal, float width, float height, float thickness, float yawDegrees, Color color,
        Transform parent, List<AdminToy> bag)
    {
        Quaternion yaw = Quaternion.AngleAxis(yawDegrees, Vector3.up);
        Vector3 edgeX = yaw * new Vector3(width * 0.5f, height * 0.5f, 0f);
        Vector3 edgeY = yaw * new Vector3(-width * 0.5f, height * 0.5f, 0f);
        Parallelogram(baseLocal, edgeX, edgeY, thickness, color, parent, bag);
    }

    /// <summary>
    /// One fully 3D-sheared PARALLELEPIPED: origin corner + THREE edge vectors (parent-local),
    /// realized as an invisible 3x3-SVD parent + a rotated unit child cube — a cube skewed on every
    /// axis at once, still 2 toys. The child scale stays exactly one: the three singular values carry
    /// ALL sizing, so there is no thickness parameter (span the third edge for thickness). Returns
    /// false (spawning nothing) for degenerate (coplanar/zero) edges.
    /// </summary>
    public static bool Parallelepiped(
        Vector3 origin, Vector3 edgeX, Vector3 edgeY, Vector3 edgeZ, Color color, Transform parent, List<AdminToy> bag)
    {
        if (!ShearMath.TryGetShearRig3D(
            edgeX, edgeY, edgeZ,
            out Vector3 center, out Quaternion parentRot, out Vector3 parentScale, out Quaternion childRot))
        {
            return false;
        }

        PrimitiveObjectToy shearParent = InvisibleParent(origin + center, parentRot, parentScale, parent, bag);
        Visible(Vector3.zero, childRot, Vector3.one, color, shearParent.Transform, bag);
        return true;
    }

    /// <summary>
    /// A 3D SHARD: a needle parallelepiped whose three edges all climb from <paramref name="baseLocal"/>
    /// to the single tip at base + <paramref name="axis"/>, splayed 120° apart around the axis (the
    /// radial offsets sum to zero, so the tip lands on the axis exactly). The waist corners sit at
    /// <paramref name="waistRadius"/> from the axis in two 60°-twisted triangles at 1/3 and 2/3 height.
    /// Both ends are genuine 3D points and the waist is a solid twisted prism, so the spike stays sharp
    /// from EVERY viewing angle — a flat diamond vanishes edge-on. <paramref name="rollDegrees"/> spins
    /// the splay triad around the axis. 2 toys.
    /// </summary>
    public static void Shard(
        Vector3 baseLocal, Vector3 axis, float waistRadius, float rollDegrees, Color color,
        Transform parent, List<AdminToy> bag)
    {
        Vector3 direction = axis.normalized;
        Vector3 seed = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
        Vector3 radial = Vector3.Cross(direction, seed).normalized * waistRadius;

        Vector3 third = axis / 3f;
        Vector3 edgeX = third + (Quaternion.AngleAxis(rollDegrees, direction) * radial);
        Vector3 edgeY = third + (Quaternion.AngleAxis(rollDegrees + 120f, direction) * radial);
        Vector3 edgeZ = third + (Quaternion.AngleAxis(rollDegrees + 240f, direction) * radial);
        Parallelepiped(baseLocal, edgeX, edgeY, edgeZ, color, parent, bag);
    }

    /// <summary>
    /// An exact filled triangle out of its 3 medial parallelograms (the ProjectMER Triangle-block
    /// algorithm): each vertex spans a parallelogram to the two adjacent edge midpoints; the union covers
    /// the triangle exactly with no spill. 6 toys. Tiles are stepped along the normal to avoid z-fighting.
    /// </summary>
    public static void Triangle(
        Vector3 pointA, Vector3 pointB, Vector3 pointC, float thickness, Color color, Transform parent, List<AdminToy> bag)
    {
        Vector3 normal = Vector3.Cross(pointB - pointA, pointC - pointA);
        Vector3 layerStep = Vector3.zero;
        if (normal.sqrMagnitude >= 0.000001f)
        {
            layerStep = normal.normalized * Mathf.Max(thickness * 0.25f, 0.002f);
        }

        Vector3 midAB = (pointA + pointB) * 0.5f;
        Vector3 midBC = (pointB + pointC) * 0.5f;
        Vector3 midCA = (pointC + pointA) * 0.5f;

        Parallelogram(pointA, midAB - pointA, midCA - pointA, thickness, color, parent, bag);
        Parallelogram(pointB + layerStep, midBC - pointB, midAB - pointB, thickness, color, parent, bag);
        Parallelogram(pointC + (layerStep * 2f), midCA - pointC, midBC - pointC, thickness, color, parent, bag);
    }
}
