using System;
using UnityEngine;

/// <summary>
/// Stand on the pad and press E to travel. The avatar owns its own transform, so the
/// warp is applied locally and replicated by the NetworkTransform.
/// A pad carrying <see cref="Choices"/> opens a list instead of leading to one place,
/// which is how the village reaches every hunting ground and dungeon from one arch.
/// </summary>
public class WarpPad : MonoBehaviour
{
    /// <summary>One line of a gate's menu.</summary>
    [Serializable]
    public struct Choice
    {
        public string Name;
        public Vector3 Destination;
        public int RequiredLevel;
    }

    public Vector3 Destination;
    public string Label = "사냥터";
    public float InteractRange = 3f;
    public float PromptHeight = 2.2f;

    /// <summary>Minimum level to step through; 0 leaves the pad open to anyone.</summary>
    public int RequiredLevel;

    /// <summary>Filled in means the pad asks where to go instead of warping straight away.</summary>
    public Choice[] Choices;

    /// <summary>The pad whose list is open, so only one window is ever up.</summary>
    static WarpPad openPad;

    bool HasMenu => Choices != null && Choices.Length > 0;

    void Awake()
    {
        // Statics outlive a play session when domain reload is off; nothing is open at start.
        openPad = null;
    }

    void Update()
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null)
        {
            return;
        }

        // Walking away closes the list, so it can never be left hanging over the world.
        if (!InRange(avatar))
        {
            if (openPad == this)
            {
                Close();
            }
            return;
        }

        if (ChatSystem.IsTyping || (ShopNpc.PanelOpen && openPad != this))
        {
            return;
        }

        if (!MetaverseUi.InteractPressed)
        {
            return;
        }

        if (HasMenu)
        {
            openPad = this;
            // The shared "a panel is up" flag, so the bag and the shop stay shut behind it.
            ShopNpc.PanelOpen = true;
            return;
        }

        if (!MeetsRequirement(avatar, RequiredLevel))
        {
            ChatSystem.Local($"{Label} 입장은 레벨 {RequiredLevel} 이상부터 가능합니다.");
            return;
        }

        avatar.Teleport(Destination);
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        var avatar = PlayerAvatar.Local;
        if (avatar == null || !InRange(avatar))
        {
            return;
        }

        if (openPad == this)
        {
            DrawMenu(avatar);
            return;
        }

        if (ShopNpc.PanelOpen)
        {
            return;
        }

        string suffix = RequiredLevel > 0 ? $"  (Lv.{RequiredLevel}+)" : "";
        string prompt = HasMenu ? $"[E] {Label} 선택" : $"[E] {Label}(으)로 이동{suffix}";
        MetaverseUi.WorldPrompt(transform.position + Vector3.up * PromptHeight, prompt);
    }

    /// <summary>One row per destination: where it goes and whether it is open yet.</summary>
    void DrawMenu(PlayerAvatar avatar)
    {
        const float width = 340f;
        const float rowHeight = 44f;

        float height = 76f + Choices.Length * rowHeight;
        var window = new Rect(MetaverseUi.Width * 0.5f - width * 0.5f, MetaverseUi.Height * 0.5f - height * 0.5f, width, height);

        GUI.Box(window, "");
        GUI.Label(new Rect(window.x + 12f, window.y + 8f, width - 24f, 22f), $"<b>{Label}</b>", MetaverseUi.Rich);
        GUI.Label(new Rect(window.x + 12f, window.y + 30f, width - 24f, 22f), "갈 곳을 고르세요.   [Esc] 닫기");

        for (int i = 0; i < Choices.Length; i++)
        {
            Choice choice = Choices[i];
            bool allowed = MeetsRequirement(avatar, choice.RequiredLevel);
            GUI.enabled = allowed;

            string note = choice.RequiredLevel > 0 ? $"Lv.{choice.RequiredLevel}+" : "제한 없음";
            bool picked = GUI.Button(new Rect(window.x + 12f, window.y + 58f + i * rowHeight, width - 24f, rowHeight - 8f),
                $"{choice.Name}   ({note})");

            GUI.enabled = true;

            if (picked)
            {
                avatar.Teleport(choice.Destination);
                Close();
                return;
            }
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            Event.current.Use();
        }
    }

    void Close()
    {
        openPad = null;
        ShopNpc.PanelOpen = false;
    }

    bool InRange(PlayerAvatar avatar)
    {
        return Vector3.Distance(avatar.transform.position, transform.position) <= InteractRange;
    }

    static bool MeetsRequirement(PlayerAvatar avatar, int level)
    {
        if (level <= 0)
        {
            return true;
        }

        var stats = avatar.GetComponent<PlayerStats>();
        return stats == null || stats.Level.Value >= level;
    }
}
