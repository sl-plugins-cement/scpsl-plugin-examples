using System.Collections.Generic;
using System.Globalization;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace ToyTricksDemo.Exhibits;

/// <summary>
/// Bay 3 — world-space TextToy, calibrated to real meters (in-game measured 2026-07-19):
///   rendered rect width [m] = DisplaySize.x × uniformScale × 0.05
///   glyph height [m] ≈ TMP size × uniformScale × 0.05.
/// Pieces, top to bottom:
///   1. two meter rulers — an edge-marker text toy (a "|" pinned to each rect edge via per-line align
///      rows collapsed with line-height=0) rendered over a physical bar of the declared length; the
///      markers land exactly on the bar ends when the calibration factor is right. Each ruler's
///      descriptive caption is a SEPARATE standard-size toy so the label stays readable and
///      collision-free no matter how long or short the measured ruler is,
///   2. a terminal page at the Crownfall DeCIRO parameters (DisplaySize (205, 11) @ scale 0.080
///      = 0.82 m physical) with TMP markup and TextAdvance.FitLine keeping lines under the safe advance,
///   3. a live line whose TextFormat contains "{0}" — updated through the Arguments SyncList
///      (cheap per-element delta) instead of re-sending the whole TextFormat string.
/// </summary>
internal static class WorldTextExhibit
{
    private static readonly Color RulerColor = new(0.85f, 0.85f, 0.9f, 1f);
    private static readonly Color PlinthColor = new(0.09f, 0.1f, 0.12f, 1f);
    private const float SafeAdvance = 195f; // 205-unit box minus ~10 units of side bearing

    /// <summary>Builds the bay and returns the live-arguments TextToy (or null if it failed to spawn).</summary>
    public static TextToy? Build(Transform root, Vector3 bayOffset, List<AdminToy> bag)
    {
        PrimitiveObjectToy bay = ExhibitTools.InvisibleParent(bayOffset, Quaternion.identity, Vector3.one, root, bag);
        Transform t = bay.Transform;

        ExhibitTools.Visible(new Vector3(0f, 0.025f, 0.1f), Quaternion.identity, new Vector3(3.8f, 0.05f, 1.1f), PlinthColor, t, bag);
        string factor = ExhibitTools.TextUnitsToMeters.ToString("0.###", CultureInfo.InvariantCulture);
        ExhibitTools.Text(new Vector3(0f, 2.05f, 0f), 1.8f, new Vector2(200f, 22f),
            $"<align=center><size=13><b>WORLD TEXT 世界文字</b></size>\n<size=8>width [m] = DisplaySize.x × scale × {factor}</size></align>", t, bag);

        Ruler(t, 1.5f, 1.0f, bag);
        Ruler(t, 1.0f, 0.5f, bag);

        // Terminal page at the DeCIRO parameters: DisplaySize (205, 11), scale 0.080 -> 0.82 m physical.
        string header = TextAdvance.FitLine("TERMINAL 205 × 11 @ 0.080", 14, bold: false, SafeAdvance);
        string status = TextAdvance.FitLine("中文全角 ≈ 1em · ASCII ≈ 0.59em", 11, bold: true, SafeAdvance);
        TextToy terminal = TextToy.Create(new Vector3(0f, 0.55f, 0f), Quaternion.identity, Vector3.one * 0.080f, t, networkSpawn: false);
        terminal.DisplaySize = new Vector2(205f, 11f);
        terminal.TextFormat =
            "<align=center><nobr><size=10><cspace=0.12><color=#7A856F>TOY TRICKS · WORLD TEXT</color></cspace></size></nobr>\n" +
            $"<size=2> </size>\n<nobr><size=14><cspace=0.08><color=#B89354>{header}</color></cspace></size></nobr>\n" +
            "<size=3> </size>\n" +
            $"<nobr><size=11><color=#58D9FF><b>{status}</b></color></size></nobr></align>";
        terminal.IsStatic = true;
        terminal.Spawn();
        bag.Add(terminal);

        // Live counter: "{0}" is substituted client-side from the Arguments SyncList.
        TextToy live = ExhibitTools.Text(new Vector3(0f, 0.22f, 0f), 0.9f, new Vector2(100f, 22f),
            "<align=center><size=8>Arguments SyncList delta 更新</size>\n<size=12><color=#55E58C>UPTIME {0} s</color></size></align>", t, bag);
        live.Arguments.Add("0");
        return live;
    }

    /// <summary>
    /// One ruler: an edge-marker toy exactly <paramref name="meters"/> wide (its only ink is a "|"
    /// pinned to each DisplaySize rect edge) over a physical bar of the same length, plus a separate
    /// fixed-size caption above — so the label never scales with (or collides into) the measured strip.
    /// </summary>
    private static void Ruler(Transform bay, float y, float meters, List<AdminToy> bag)
    {
        const float DisplayUnits = 100f;
        float scale = ExhibitTools.TextScaleFor(meters, DisplayUnits);

        ExhibitTools.Text(new Vector3(0f, y + 0.17f, 0f), 0.9f, new Vector2(DisplayUnits, 22f),
            $"<align=center><nobr><size=8>{DisplayUnits:0} × {scale:0.000}</size></nobr>\n" +
            $"<nobr><size=11><b>= {meters:0.00} m</b></size></nobr></align>", bay, bag);

        // Edge markers: two "|" rows overlaid via line-height=0, one left-aligned, one right-aligned —
        // the ink spans the full DisplaySize rect, making its true width visible.
        ExhibitTools.Text(new Vector3(0f, y, 0f), meters, new Vector2(DisplayUnits, 16f),
            "<align=left><size=14><b>|</b></size><line-height=0>\n" +
            "<align=right><size=14><b>|</b></size></align>", bay, bag);

        ExhibitTools.Visible(new Vector3(0f, y - 0.1f, 0f), Quaternion.identity, new Vector3(meters, 0.02f, 0.02f), RulerColor, bay, bag);
        ExhibitTools.Visible(new Vector3(-meters * 0.5f, y - 0.07f, 0f), Quaternion.identity, new Vector3(0.02f, 0.08f, 0.02f), RulerColor, bay, bag);
        ExhibitTools.Visible(new Vector3(meters * 0.5f, y - 0.07f, 0f), Quaternion.identity, new Vector3(0.02f, 0.08f, 0.02f), RulerColor, bay, bag);
    }
}
