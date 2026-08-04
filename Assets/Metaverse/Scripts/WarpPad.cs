using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stand on the pad and press E to travel. The avatar owns its own transform, so the
/// warp is applied locally and replicated by the NetworkTransform.
/// </summary>
public class WarpPad : MonoBehaviour
{
    public Vector3 Destination;
    public string Label = "Hunting Field";
    public float InteractRange = 3f;
    public float PromptHeight = 2.2f;

    void Update()
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null || !InRange(avatar) || ChatSystem.IsTyping || ShopNpc.PanelOpen)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            avatar.Teleport(Destination);
        }
    }

    void OnGUI()
    {
        var avatar = PlayerAvatar.Local;
        var camera = Camera.main;
        if (avatar == null || camera == null || !InRange(avatar) || ShopNpc.PanelOpen)
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * PromptHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var prompt = new GUIContent($"[E] Warp to {Label}");
        Vector2 size = GUI.skin.box.CalcSize(prompt);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y, size.x, size.y), prompt);
    }

    bool InRange(PlayerAvatar avatar)
    {
        return Vector3.Distance(avatar.transform.position, transform.position) <= InteractRange;
    }
}
