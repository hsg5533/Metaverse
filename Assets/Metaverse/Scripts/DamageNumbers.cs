using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floating numbers over whatever just took a hit. Fed by a small RPC fired right where the
/// damage is actually computed (Monster.TakeDamage, PlayerStats.TakeDamage/TakeDuelDamage) -
/// not by diffing Hp itself, since Hp also moves for reasons that are not a hit (re-levelling,
/// a level-up heal, a respawn), and diffing would show a false number for every one of those.
/// </summary>
public static class DamageNumbers
{
    class Entry
    {
        public Vector3 Position;
        public int Amount;
        public bool Taken;
        public float EndTime;
    }

    const float Lifetime = 0.8f;
    const float RiseSpeed = 0.7f;

    static readonly Color TextColor = new(0.92f, 0.16f, 0.16f);

    static readonly List<Entry> entries = new();
    static GUIStyle style;

    static GUIStyle Style()
    {
        return style ??= new GUIStyle(GUI.skin.label)
        {
            richText = true,
            alignment = TextAnchor.MiddleCenter,
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
    }

    /// <summary>
    /// Call from wherever a hit already lands. "taken" marks damage the local view should read
    /// as a loss - a minus sign in front - as opposed to damage dealt out, which has none.
    /// </summary>
    public static void Add(Vector3 worldPosition, int amount, bool taken = false)
    {
        if (amount <= 0)
        {
            return;
        }

        entries.Add(new Entry { Position = worldPosition, Amount = amount, Taken = taken, EndTime = Time.time + Lifetime });
    }

    /// <summary>Drawn once from MetaverseHUD, inside the same world-space depth block as the name tags.</summary>
    public static void Draw()
    {
        if (entries.Count == 0)
        {
            return;
        }

        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            float remaining = entry.EndTime - Time.time;
            if (remaining <= 0f)
            {
                entries.RemoveAt(i);
                continue;
            }

            float age = Lifetime - remaining;
            Vector3 world = entry.Position + Vector3.up * (age * RiseSpeed);
            Vector3 screenPoint = MetaverseUi.ScreenPoint(camera, world);
            if (screenPoint.z <= 0f)
            {
                continue;
            }

            Color previous = GUI.color;
            GUI.color = new Color(TextColor.r, TextColor.g, TextColor.b, Mathf.Clamp01(remaining / Lifetime));

            float y = MetaverseUi.Height - screenPoint.y;
            string text = entry.Taken ? $"-{entry.Amount}" : entry.Amount.ToString();
            GUI.Label(new Rect(screenPoint.x - 90f, y - 20f, 180f, 34f), text, Style());

            GUI.color = previous;
        }
    }
}
