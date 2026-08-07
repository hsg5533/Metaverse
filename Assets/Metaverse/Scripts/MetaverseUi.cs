using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared IMGUI bits: the Korean font, the two label styles every panel wants, the floating
/// world prompt, and the player lookups the server systems all need.
/// </summary>
public static class MetaverseUi
{
    /// <summary>
    /// Fonts that carry Hangul, in the order they are preferred. Windows names first, then the
    /// ones a phone ships with.
    /// </summary>
    static readonly string[] Candidates =
    {
        "Malgun Gothic",
        "맑은 고딕",
        "NanumGothic",
        "Noto Sans KR",
        "Noto Sans CJK KR",
        "Noto Sans CJK",
        "Roboto",
        "Gulim",
        "Dotum",
        "Batang",
    };

    static Font font;
    static bool searched;
    static GUIStyle rich;
    static GUIStyle centered;

    /// <summary>The tallest window the game draws; the screen never scales below it.</summary>
    public const float SmallestUsableHeight = 340f;

    public static float Scale => AutoScale;

    /// <summary>
    /// How much bigger everything has to be drawn to end up the same physical size it is on a
    /// monitor. Pixel counts are the wrong measure: this phone packs 480 of them to the inch,
    /// so a button that looks fine on a desk is a millimetre tall in the hand.
    /// Android's answer is 160 dots to the inch; this uses 200, because a phone is held a
    /// forearm closer than a monitor and does not need the full physical size. Held back
    /// further so a window never grows taller than the screen it sits on.
    /// </summary>
    static float AutoScale
    {
        get
        {
            if (!Application.isMobilePlatform && Screen.dpi <= 200f)
            {
                return 1f;
            }

            float density = Mathf.Max(1f, Screen.dpi / 200f);
            float fits = Mathf.Min(Screen.width, Screen.height) / SmallestUsableHeight;
            return Mathf.Min(density, fits);
        }
    }

    /// <summary>The screen as the interface sees it: pixels divided by the scale.</summary>
    public static float Width => Screen.width / Scale;
    public static float Height => Screen.height / Scale;

    /// <summary>A world point in the space the interface is drawn in.</summary>
    public static Vector3 ScreenPoint(Camera camera, Vector3 worldPosition)
    {
        float scale = Scale;
        Vector3 point = camera.WorldToScreenPoint(worldPosition);
        return new Vector3(point.x / scale, point.y / scale, point.z);
    }

    /// <summary>Call at the top of an OnGUI; after the first frame it is a single assignment.</summary>
    public static void ApplyFont()
    {
        GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);

        if (!searched)
        {
            searched = true;
            // Smaller on a phone: the whole interface is already blown up to stay thumb
            // sized, and text does not need the same treatment to stay readable.
            font = Font.CreateDynamicFontFromOSFont(PreferredFonts(), Application.isMobilePlatform ? 11 : 14);
        }

        if (font != null && GUI.skin.font != font)
        {
            GUI.skin.font = font;
        }
    }

    /// <summary>
    /// The wanted fonts that this machine actually has, or everything it has when none of them
    /// match: asking for a font by a name the system never heard of gets a font with no glyphs
    /// in it, which draws as nothing at all.
    /// </summary>
    static string[] PreferredFonts()
    {
        string[] installed = Font.GetOSInstalledFontNames();
        if (installed == null || installed.Length == 0)
        {
            return Candidates;
        }

        var wanted = new System.Collections.Generic.List<string>();
        foreach (string candidate in Candidates)
        {
            foreach (string name in installed)
            {
                if (name.Contains(candidate, System.StringComparison.OrdinalIgnoreCase))
                {
                    wanted.Add(name);
                }
            }
        }

        if (wanted.Count == 0)
        {
            Debug.LogWarning("[Metaverse] no preferred font on this device; falling back to all of them.");
            return installed;
        }

        // The rest of the system's fonts stay on as fallbacks for anything the first one lacks.
        wanted.AddRange(installed);
        return wanted.ToArray();
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

        Vector3 screenPoint = ScreenPoint(camera, worldPosition);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var content = new GUIContent(text);
        Vector2 size = GUI.skin.box.CalcSize(content);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Height - screenPoint.y, size.x, size.y), content);
    }

    /// <summary>E on the keyboard. Every station and node reads this one check.</summary>
    public static bool InteractPressed =>
        (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) || MobileInput.ConsumeInteract();

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
