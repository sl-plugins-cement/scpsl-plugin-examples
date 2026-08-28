namespace ToyTricksDemo;

/// <summary>
/// Conservative Liberation Sans/TMP horizontal-advance estimate in TextToy display units, ported from
/// the in-game-verified Crownfall DeCIRO console (reinforcements-system CrownfallIntroScene). Use it to
/// fit dynamic strings inside a DisplaySize box before they word-wrap or spill: keep the estimated
/// advance under DisplaySize.x minus ~10 units of side bearing (e.g. 195 inside a 205 box).
/// CJK characters count as a full em, ASCII ≈ 0.59 em, so bilingual strings budget correctly.
/// </summary>
public static class TextAdvance
{
    /// <summary>Estimated advance of <paramref name="value"/> in TextToy display units at TMP size <paramref name="size"/>.</summary>
    public static float Estimate(string? value, int size, bool bold)
    {
        float em = 0f;
        foreach (char character in value ?? string.Empty)
        {
            em += character switch
            {
                ' ' => 0.34f,
                '·' or '/' or ':' or '-' => 0.48f,
                <= '\u007f' => 0.59f,
                _ => 1f,
            };
        }

        return em * size * (bold ? 1.03f : 1f);
    }

    /// <summary>Truncates <paramref name="value"/> with an ellipsis so its estimated advance fits <paramref name="safeAdvance"/>.</summary>
    public static string FitLine(string? value, int size, bool bold, float safeAdvance)
    {
        value ??= string.Empty;
        if (Estimate(value, size, bold) <= safeAdvance)
        {
            return value;
        }

        string candidate = value;
        while (candidate.Length > 1 && Estimate(candidate + "…", size, bold) > safeAdvance)
        {
            candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
        }

        return candidate + "…";
    }
}
