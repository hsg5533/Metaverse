using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared behaviour for the things you walk up to and press E on: a prompt while close,
/// a panel while open. Subclasses only fill in the panel contents.
/// </summary>
public abstract class InteractStation : MonoBehaviour
{
    public string Title = "시설";
    public float InteractRange = 3.5f;
    public float PromptHeight = 2.2f;
    public Vector2 PanelSize = new(340f, 210f);

    bool open;

    protected abstract void DrawPanel(PlayerAvatar player);

    void OnDisable()
    {
        Close();
    }

    void Update()
    {
        if (PlayerAvatar.Local == null || !InRange())
        {
            Close();
            return;
        }

        if (MetaverseUi.InteractPressed && !ChatSystem.IsTyping && !PlayerInventory.WindowOpen)
        {
            open = !open;
            ShopNpc.PanelOpen = open;
        }
    }

    protected void Close()
    {
        if (open)
        {
            open = false;
            ShopNpc.PanelOpen = false;
        }
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        var player = PlayerAvatar.Local;
        if (player == null)
        {
            return;
        }

        if (open)
        {
            var area = new Rect(MetaverseUi.Width * 0.5f - PanelSize.x * 0.5f, MetaverseUi.Height * 0.5f - PanelSize.y * 0.5f, PanelSize.x, PanelSize.y);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"<b>{Title}</b>", MetaverseUi.Rich);
            DrawPanel(player);
            GUILayout.Space(4);
            if (GUILayout.Button("닫기  [E]"))
            {
                Close();
            }
            GUILayout.EndArea();
            return;
        }

        if (!InRange())
        {
            return;
        }

        MetaverseUi.WorldPrompt(transform.position + Vector3.up * PromptHeight, $"[E] {Title}");
    }

    bool InRange()
    {
        return Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) <= InteractRange;
    }
}
