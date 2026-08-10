using UnityEngine;

/// <summary>
/// The mirror in the village: walk up, press E, and pick what you look like. Everything here
/// is a place in a palette rather than a colour, so the server has nothing to trust and the
/// choice survives in the save file as four small numbers.
/// </summary>
public class Dresser : InteractStation
{
    const float SwatchSize = 28f;
    const float SwatchGap = 4f;
    const float LabelWidth = 62f;

    /// <summary>A row is one swatch tall with a little air around it.</summary>
    const float RowHeight = SwatchSize + 8f;

    /// <summary>
    /// Everything that is not a row: the title, the close button the station puts underneath,
    /// and the margins around both. Budgeted with room to spare - the panel that runs over is
    /// the one whose close button falls off the bottom, and a mirror with no way out of it is
    /// worse than a mirror with a gap at the end.
    /// </summary>
    const float Chrome = 120f;

    void Awake()
    {
        Title = "거울";
        PanelSize = new Vector2(360f, Chrome + 4f * RowHeight);
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        int body = player.BodyTint.Value;
        int pants = player.PantsTint.Value;
        int hair = player.HairTint.Value;
        int style = player.HairStyle.Value;

        float width = PanelSize.x - 24f;
        int pickedBody = Row(width, "상의", PlayerAvatar.SwatchCount, body, PlayerAvatar.Swatch);
        int pickedPants = Row(width, "바지", PlayerAvatar.SwatchCount, pants, PlayerAvatar.Swatch);
        int pickedHair = Row(width, "머리색", PlayerAvatar.SwatchCount, hair, PlayerAvatar.HairSwatch);

        int styles = player.HairStyles != null ? player.HairStyles.Length : 1;
        int pickedStyle = Row(width, "머리", styles, style, null);

        if (pickedBody != body || pickedPants != pants || pickedHair != hair || pickedStyle != style)
        {
            player.SetLookRpc(pickedBody, pickedPants, pickedHair, pickedStyle);
        }
    }

    /// <summary>
    /// One line of choices, named on the left, returning what is chosen after the click. A null
    /// palette means the row is not about colour and gets numbered buttons: styles have no swatch.
    /// </summary>
    static int Row(float width, string label, int count, int chosen, System.Func<int, Color> palette)
    {
        var row = GUILayoutUtility.GetRect(width, RowHeight);
        GUI.Label(new Rect(row.x, row.y + 4f, LabelWidth, 22f), label);

        int picked = chosen;
        for (int i = 0; i < count; i++)
        {
            var slot = new Rect(row.x + LabelWidth + i * (SwatchSize + SwatchGap), row.y + 2f, SwatchSize, SwatchSize);

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
