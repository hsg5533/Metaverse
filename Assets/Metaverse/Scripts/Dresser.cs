using UnityEngine;

/// <summary>
/// The mirror in the village: walk up, press E, and pick what you look like. Everything here
/// is a place in a palette rather than a colour, so the server has nothing to trust and the
/// choice survives in the save file as four small numbers.
/// </summary>
public class Dresser : InteractStation
{
    const float SwatchSize = 30f;
    const float SwatchGap = 4f;

    void Awake()
    {
        Title = "거울";
        // Four rows of choices, each with a line naming it, and the close button underneath.
        PanelSize = new Vector2(360f, 96f + 4f * (22f + SwatchSize + SwatchGap));
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        int body = player.BodyTint.Value;
        int pants = player.PantsTint.Value;
        int hair = player.HairTint.Value;
        int style = player.HairStyle.Value;

        float width = PanelSize.x - 24f;
        int pickedBody = Swatches(width, "상의", PlayerAvatar.SwatchCount, body, PlayerAvatar.Swatch);
        int pickedPants = Swatches(width, "바지", PlayerAvatar.SwatchCount, pants, PlayerAvatar.Swatch);
        int pickedHair = Swatches(width, "머리 색", PlayerAvatar.SwatchCount, hair, PlayerAvatar.HairSwatch);

        int styles = player.HairStyles != null ? player.HairStyles.Length : 1;
        int pickedStyle = Swatches(width, "머리 모양", styles, style, null);

        if (pickedBody != body || pickedPants != pants || pickedHair != hair || pickedStyle != style)
        {
            player.SetLookRpc(pickedBody, pickedPants, pickedHair, pickedStyle);
        }
    }

    /// <summary>
    /// One row of choices, returning what is chosen after the click. A null palette means the
    /// row is not about colour and gets numbered buttons instead: hair styles have no swatch.
    /// </summary>
    static int Swatches(float width, string label, int count, int chosen, System.Func<int, Color> palette)
    {
        GUILayout.Label(label);

        var row = GUILayoutUtility.GetRect(width, SwatchSize + SwatchGap);
        int picked = chosen;

        for (int i = 0; i < count; i++)
        {
            var slot = new Rect(row.x + i * (SwatchSize + SwatchGap), row.y, SwatchSize, SwatchSize);

            // The one being worn is drawn a size larger, which is the only marking IMGUI gives
            // for free that survives being a coloured box.
            if (i == chosen)
            {
                slot = new Rect(slot.x - 2f, slot.y - 2f, slot.width + 4f, slot.height + 4f);
            }

            Color previous = GUI.backgroundColor;
            if (palette != null)
            {
                GUI.backgroundColor = palette(i);
            }

            if (GUI.Button(slot, palette != null ? GUIContent.none : new GUIContent(i == 0 ? "-" : i.ToString())))
            {
                picked = i;
            }

            GUI.backgroundColor = previous;
        }

        return picked;
    }
}
