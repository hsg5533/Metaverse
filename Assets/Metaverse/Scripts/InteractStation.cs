using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shared behaviour for the things you walk up to and press E on: a prompt while close,
/// a panel while open. Subclasses only fill in the panel contents.
/// </summary>
public abstract class InteractStation : MonoBehaviour
{
    public string Title = "Station";
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

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame && !ChatSystem.IsTyping)
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
        var player = PlayerAvatar.Local;
        if (player == null)
        {
            return;
        }

        if (open)
        {
            var area = new Rect(Screen.width * 0.5f - PanelSize.x * 0.5f, Screen.height * 0.5f - PanelSize.y * 0.5f, PanelSize.x, PanelSize.y);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"<b>{Title}</b>", RichLabel());
            DrawPanel(player);
            GUILayout.Space(4);
            if (GUILayout.Button("Close  [E]"))
            {
                Close();
            }
            GUILayout.EndArea();
            return;
        }

        var camera = Camera.main;
        if (camera == null || !InRange())
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * PromptHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var prompt = new GUIContent($"[E] {Title}");
        Vector2 size = GUI.skin.box.CalcSize(prompt);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y, size.x, size.y), prompt);
    }

    bool InRange()
    {
        return Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) <= InteractRange;
    }

    static GUIStyle richLabel;

    protected static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
