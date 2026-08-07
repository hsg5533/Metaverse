using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Touch controls: a stick on the left half to walk with, buttons on the right to attack,
/// jump and interact, and a drag anywhere else to look around. Drawn with IMGUI like the
/// rest of the interface, so there is no canvas to wire up.
/// The rest of the game reads the statics and never knows a finger was involved.
/// </summary>
public class MobileInput : MonoBehaviour
{
    /// <summary>There is a screen to touch, so the controls are worth drawing.</summary>
    public static bool Active { get; private set; }

    /// <summary>Where the stick is pushed, in the same shape as the WASD input.</summary>
    public static Vector2 Move { get; private set; }

    /// <summary>How far the look drag moved this frame, in pixels.</summary>
    public static Vector2 Look { get; private set; }

    static bool jump;
    static bool attack;
    static bool interact;

    /// <summary>Each press is handed out once, the same way a key press is.</summary>
    public static bool ConsumeJump() => Take(ref jump);
    public static bool ConsumeAttack() => Take(ref attack);
    public static bool ConsumeInteract() => Take(ref interact);

    /// <summary>
    /// Keys the menu has tapped on the player's behalf. Every shortcut in the game checks
    /// this next to its own key, so a phone reaches everything a keyboard does.
    /// </summary>
    static readonly HashSet<Key> tapped = new();

    public static void Press(Key key)
    {
        tapped.Add(key);
    }

    /// <summary>Handed out once, like a key press.</summary>
    public static bool Pressed(Key key)
    {
        return tapped.Remove(key);
    }

    /// <summary>
    /// Puts itself into the running game on a device that can be touched, so the controls do
    /// not depend on the scene having been rebuilt.
    /// </summary>
    [RuntimeInitializeOnLoadMethod]
    static void Spawn()
    {
        if (!Application.isMobilePlatform && Touchscreen.current == null)
        {
            return;
        }

        // Landscape, always, and always the same way round, so the front camera and whatever
        // it punches out of the display stay on the left. Setting it here rather than in the
        // player settings, which the open editor overwrites from its own state whenever it
        // feels like it.
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = false;
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        if (FindAnyObjectByType<MobileInput>() == null)
        {
            new GameObject("MobileInput") { hideFlags = HideFlags.HideInHierarchy }.AddComponent<MobileInput>();
        }
    }

    const float StickRadius = 88f;
    const float ButtonSize = 111f;

    /// <summary>
    /// Where the stick sits when nobody is holding it: bottom left. In touch space, where y
    /// counts up from the bottom of the screen, the same as the fingers it is compared with.
    /// </summary>
    static Vector2 StickHome => new Vector2(24f + StickRadius, 24f + StickRadius);

    const int NoRole = -1;
    const int StickRole = 0;
    const int LookRole = 4;   // 1, 2 and 3 are the three buttons
    const int SpentRole = 5;  // touched a button and then dragged: it does nothing now

    /// <summary>How far a finger may wander and still count as a tap.</summary>
    const float TapSlack = 22f;

    // What each touch slot is doing, kept for as long as that finger stays down. Read from
    // whatever is pressed right now, so there is no first frame to miss.
    int[] role;
    Vector2[] lastPoint;
    Vector2[] startPoint;
    bool stickHeld;

    void Update()
    {
        var screen = Touchscreen.current;
        Active = screen != null;
        if (!Active)
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            return;
        }

        // A window covers the controls; a tap meant for it must not also swing or interact.
        if (MetaverseUi.WindowOpen)
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            ClearRoles();
            return;
        }

        int count = screen.touches.Count;
        if (role == null || role.Length != count)
        {
            role = new int[count];
            lastPoint = new Vector2[count];
            startPoint = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                role[i] = NoRole;
            }
        }

        Vector2 move = Vector2.zero;
        Vector2 look = Vector2.zero;
        bool held = false;

        for (int i = 0; i < count; i++)
        {
            var touch = screen.touches[i];
            if (!touch.press.isPressed)
            {
                // Interact fires on release, and only if the finger stayed put: the button
                // sits in the middle of the half of the screen people swipe to look around,
                // and a swipe that happens to start on it is not a press.
                if (role[i] == 3)
                {
                    interact = true;
                }

                role[i] = NoRole;
                continue;
            }

            Vector2 point = touch.position.ReadValue() / MetaverseUi.Scale;

            // A finger keeps the job it landed on, however far it then wanders. Dragging off
            // the stick must not start turning the camera, and sweeping the camera across the
            // stick must not start walking.
            if (role[i] == NoRole)
            {
                role[i] = RoleAt(point);
                lastPoint[i] = point;
                startPoint[i] = point;
                PressButton(role[i]);
            }

            // Dragged off a button: it was a swipe, so nothing happens when the finger lifts.
            if (role[i] >= 1 && role[i] <= 3 && Vector2.Distance(point, startPoint[i]) > TapSlack)
            {
                role[i] = SpentRole;
            }

            if (role[i] == StickRole)
            {
                move = Vector2.ClampMagnitude((point - StickHome) / StickRadius, 1f);
                held = true;
            }
            else if (role[i] == LookRole)
            {
                look += point - lastPoint[i];
            }

            lastPoint[i] = point;
        }

        stickHeld = held;
        Move = move;
        Look = look;
    }

    /// <summary>Forgets what every finger was doing, so nothing carries over.</summary>
    void ClearRoles()
    {
        if (role == null)
        {
            return;
        }

        for (int i = 0; i < role.Length; i++)
        {
            role[i] = NoRole;
        }
    }

    /// <summary>What a finger landing here takes charge of.</summary>
    static int RoleAt(Vector2 point)
    {
        var flipped = new Vector2(point.x, MetaverseUi.Height - point.y);
        for (int i = 0; i < 3; i++)
        {
            Rect rect = ButtonRect(i);
            rect.xMin -= 10f;
            rect.yMin -= 10f;
            rect.xMax += 10f;
            rect.yMax += 10f;

            if (rect.Contains(flipped))
            {
                return i + 1;
            }
        }

        // The pad only, not the whole left half; everywhere else looks around.
        return Vector2.Distance(point, StickHome) < StickRadius * 2f ? StickRole : LookRole;
    }

    /// <summary>
    /// Attack and jump fire the moment they are touched - they live in the corner, where a
    /// look drag never starts, and they are worth no delay. Interact waits for the release.
    /// </summary>
    static void PressButton(int assigned)
    {
        if (assigned == 1) { attack = true; }
        else if (assigned == 2) { jump = true; }
    }

    /// <summary>Attack, jump, interact: a cluster in the bottom right corner, thumb sized.</summary>
    static Rect ButtonRect(int index)
    {
        // Off the very corner: a thumb curls inwards and the far corner is the hardest
        // place on the screen to reach.
        float right = MetaverseUi.Width - 46f;
        float bottom = MetaverseUi.Height - 40f;

        return index switch
        {
            // Attack under the thumb, jump beside it with room between, and interact set
            // well clear of both: pressing E mid fight is worse than reaching for it.
            0 => new Rect(right - ButtonSize, bottom - ButtonSize, ButtonSize, ButtonSize),
            1 => new Rect(right - ButtonSize * 2.35f, bottom - ButtonSize * 0.85f, ButtonSize, ButtonSize),
            _ => new Rect(right - ButtonSize, bottom - ButtonSize * 2.2f, ButtonSize, ButtonSize),
        };
    }

    /// <summary>
    /// A white disc with a soft edge, drawn once into a texture. IMGUI has no circle of its
    /// own and every control here is round.
    /// </summary>
    static Texture2D Disc()
    {
        if (disc != null)
        {
            return disc;
        }

        const int size = 64;
        const float radius = size * 0.5f;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        disc.SetPixels32(pixels);
        disc.Apply();
        return disc;
    }

    static Texture2D disc;

    static void DrawDisc(Rect rect, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, Disc());
        GUI.color = previous;
    }

    void OnGUI()
    {
        if (!Active || PlayerAvatar.Local == null || MetaverseUi.WindowOpen)
        {
            return;
        }

        MetaverseUi.ApplyFont();

        // Behind every window, so an open bag is never fought over.
        GUI.depth = MetaverseUi.WorldDepth;

        string[] labels = { "공격", "점프", "E" };
        for (int i = 0; i < labels.Length; i++)
        {
            Rect rect = ButtonRect(i);
            DrawDisc(rect, new Color(0f, 0f, 0f, 0.35f));
            DrawDisc(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f), new Color(1f, 1f, 1f, 0.3f));
            GUI.Label(rect, labels[i], MetaverseUi.Centered);
        }

        // Always in its corner, so the thumb knows where to land without looking.
        Vector2 centre = StickHome;
        float top = MetaverseUi.Height - centre.y;

        DrawDisc(new Rect(centre.x - StickRadius, top - StickRadius, StickRadius * 2f, StickRadius * 2f),
            new Color(0f, 0f, 0f, stickHeld ? 0.45f : 0.3f));

        Vector2 knob = centre + Move * StickRadius * 0.7f;
        const float knobRadius = 32f;
        DrawDisc(new Rect(knob.x - knobRadius, MetaverseUi.Height - knob.y - knobRadius, knobRadius * 2f, knobRadius * 2f),
            new Color(1f, 1f, 1f, 0.55f));
    }

    static bool Take(ref bool pressed)
    {
        if (!pressed)
        {
            return false;
        }

        pressed = false;
        return true;
    }
}
