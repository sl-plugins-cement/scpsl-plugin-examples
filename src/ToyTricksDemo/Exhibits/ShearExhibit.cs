using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// Bay 1 — replicated SHEAR. Five side-by-side pieces, left to right:
///   1. control: a single rotated cube (all one toy can ever be — R·S, no shear),
///   2. a sheared parallelogram panel (SVD rig: invisible non-uniform-scaled parent + rotated child, 2 toys),
///   3. a pointed spike = ONE sheared rhombus whose far corner is the tip (2 toys — the SerpentsWall blade),
///   4. an exact filled triangle from its 3 medial parallelograms (6 toys — the ProjectMER Triangle block),
///   5. a 3D SHARD: three edge vectors → 3x3-SVD parallelepiped, a needle skewed on EVERY axis at once
///      (2 toys) — sharp from all viewing angles, where the single-plane spike vanishes edge-on.
/// Pieces 2–3 stay single-plane on purpose: they are the didactic baseline the 3D rig extends.
/// All in-plane coordinates are real meters in the bay's local XY plane facing the viewer.
/// </summary>
internal static class ShearExhibit
{
    private static readonly Color PanelColor = new(0.16f, 0.62f, 0.68f, 1f);
    private static readonly Color BladeColor = new(0.22f, 0.72f, 0.55f, 1f);
    private static readonly Color TriangleColor = new(0.72f, 0.55f, 0.2f, 1f);
    private static readonly Color ShardColor = new(0.62f, 0.3f, 0.72f, 1f);
    private static readonly Color ControlColor = new(0.5f, 0.5f, 0.55f, 1f);
    private static readonly Color PlinthColor = new(0.09f, 0.1f, 0.12f, 1f);
    private const float Thickness = 0.04f;

    public static void Build(Transform root, Vector3 bayOffset, List<AdminToy> bag)
    {
        PrimitiveObjectToy bay = ExhibitTools.InvisibleParent(bayOffset, Quaternion.identity, Vector3.one, root, bag);
        Transform t = bay.Transform;

        ExhibitTools.Visible(new Vector3(0.5f, 0.025f, 0.1f), Quaternion.identity, new Vector3(4.9f, 0.05f, 1.1f), PlinthColor, t, bag);
        ExhibitTools.Text(new Vector3(0.5f, 2.05f, 0f), 1.9f, new Vector2(200f, 14f),
            "<align=center><size=13><b>SHEAR 剪切</b></size>\n<size=8>parent Σ (non-uniform scale) × child Vᵀ (rotation) replicates</size></align>", t, bag);

        // 1. Control: one toy = rotated box only. 25° roll, no skew possible.
        ExhibitTools.Visible(new Vector3(-1.45f, 0.62f, 0f), Quaternion.AngleAxis(25f, Vector3.forward),
            new Vector3(0.65f, 0.35f, Thickness), ControlColor, t, bag);
        Caption(t, -1.45f, "1 toy: rotate only\n单体只能旋转", bag);

        // 2. Parallelogram panel: origin corner + two edge vectors, factored by ShearMath.
        ExhibitTools.Parallelogram(new Vector3(-1.0f, 0.25f, 0f), new Vector3(0.85f, 0f, 0f), new Vector3(0.35f, 0.8f, 0f),
            Thickness, PanelColor, t, bag);
        Caption(t, -0.45f, "shear rig, 2 toys\n平行四边形", bag);

        // 3. Spike: one sheared rhombus; base corner planted, u+v IS the tip (no stack of cubes).
        // Pointiness = the angle between the two edges: the more nearly PARALLEL u and v are, the more
        // acute the tip (they only must not be collinear, or the SVD rig degenerates). These edges are
        // ~11° apart -> a needle; widen the split for a stockier blade.
        ExhibitTools.Parallelogram(new Vector3(0.5f, 0.05f, 0f), new Vector3(-0.08f, 0.62f, 0f), new Vector3(0.14f, 1.05f, 0f),
            Thickness, BladeColor, t, bag);
        Caption(t, 0.5f, "spike = 1 rhombus\n尖刺 2 toys", bag);

        // 4. Exact triangle: 3 medial parallelograms, zero spill (ProjectMER Triangle block, 6 toys).
        ExhibitTools.Triangle(new Vector3(1.05f, 0.2f, 0f), new Vector3(1.85f, 0.2f, 0f), new Vector3(1.35f, 1.0f, 0f),
            Thickness * 0.5f, TriangleColor, t, bag);
        Caption(t, 1.45f, "3 medial paras, 6 toys\n三角精确覆盖", bag);

        // 5. 3D shard: the 3x3-SVD rig — three edges climbing to one tip, splayed around the axis, so
        // the needle is skewed on every axis at once and keeps its point from ANY viewing direction
        // (walk around it vs piece 3, which disappears edge-on). Leaning axis = the "spikey" pose.
        ExhibitTools.Shard(new Vector3(2.5f, 0.05f, 0f), new Vector3(0.28f, 1.5f, 0.16f), 0.13f, 20f,
            ShardColor, t, bag);
        Caption(t, 2.5f, "3D shard: 3x3 SVD, 2 toys\n全轴剪切 任何角度都尖", bag);
    }

    private static void Caption(Transform bay, float x, string text, List<AdminToy> bag)
    {
        // Small DisplaySize (100) = big scale = readable glyphs at pedestal-caption size. 0.9 m keeps
        // neighboring captions clear of each other (pedestals are ~0.95 m apart).
        ExhibitTools.Text(new Vector3(x, 0.16f, -0.35f), 0.9f, new Vector2(100f, 34f),
            $"<align=center><size=11>{text}</size></align>", bay, bag);
    }
}
