using UnityEngine;

/// <summary>
/// The built-in IMGUI font has no Hangul, so every panel would draw boxes. This pulls a
/// Korean face out of the operating system once and hands it to the skin.
/// </summary>
public static class MetaverseUi
{
    static readonly string[] Candidates =
    {
        "Malgun Gothic",
        "맑은 고딕",
        "NanumGothic",
        "Noto Sans KR",
        "Gulim",
        "Dotum",
        "Batang",
    };

    static Font font;
    static bool searched;

    /// <summary>Call at the top of an OnGUI; after the first frame it is a single assignment.</summary>
    public static void ApplyFont()
    {
        if (!searched)
        {
            searched = true;
            font = Font.CreateDynamicFontFromOSFont(Candidates, 14);

            if (font == null)
            {
                Debug.LogWarning("[Metaverse] no Korean system font found; text may draw as boxes.");
            }
        }

        if (font != null && GUI.skin.font != font)
        {
            GUI.skin.font = font;
        }
    }
}
