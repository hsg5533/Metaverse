using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared IMGUI bits: the Korean font, the two label styles every panel wants, the floating
/// world prompt, and the player lookups the server systems all need.
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
    static GUIStyle rich;
    static GUIStyle centered;

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

    public static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };

    public static GUIStyle Centered =>
        centered ??= new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter };

    /// <summary>
    /// Depth for anything drawn out in the world - name tags, prompts, health bars. Higher
    /// draws further back, so a window opened on top of them always wins.
    /// </summary>
    public const int WorldDepth = 10;

    /// <summary>A filled bar on a dark backing: health over a head, health on the HUD.</summary>
    public static void Bar(Rect rect, float fill, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = colour;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>A label box floating over a point in the world, e.g. "[E] 채집".</summary>
    public static void WorldPrompt(Vector3 worldPosition, string text)
    {
        GUI.depth = WorldDepth;

        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(worldPosition);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var content = new GUIContent(text);
        Vector2 size = GUI.skin.box.CalcSize(content);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y, size.x, size.y), content);
    }

    /// <summary>E on the keyboard. Every station and node reads this one check.</summary>
    public static bool InteractPressed =>
        Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

    /// <summary>The avatar a client owns, or null when it has not spawned.</summary>
    public static NetworkObject PlayerObject(ulong clientId)
    {
        var manager = NetworkManager.Singleton;
        return manager != null && manager.ConnectedClients.TryGetValue(clientId, out var client)
            ? client.PlayerObject
            : null;
    }

    public static PlayerStats StatsOf(ulong clientId)
    {
        var player = PlayerObject(clientId);
        return player != null ? player.GetComponent<PlayerStats>() : null;
    }

    public static string NameOf(ulong clientId)
    {
        var player = PlayerObject(clientId);
        var avatar = player != null ? player.GetComponent<PlayerAvatar>() : null;
        return avatar != null ? avatar.Nickname.Value.ToString() : "플레이어";
    }
}
