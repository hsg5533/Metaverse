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

    /// <summary>Minimum level to step through; 0 leaves the pad open to anyone.</summary>
    public int RequiredLevel;

    void Update()
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null || !InRange(avatar) || ChatSystem.IsTyping || ShopNpc.PanelOpen)
        {
            return;
        }

        if (MetaverseUi.InteractPressed)
        {
            if (!MeetsRequirement(avatar))
            {
                ChatSystem.Local($"{Label} 입장은 레벨 {RequiredLevel} 이상부터 가능합니다.");
                return;
            }

            avatar.Teleport(Destination);
        }
    }

    void OnGUI()
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null || !InRange(avatar) || ShopNpc.PanelOpen)
        {
            return;
        }

        string suffix = RequiredLevel > 0 ? $"  (Lv.{RequiredLevel}+)" : "";
        MetaverseUi.WorldPrompt(transform.position + Vector3.up * PromptHeight, $"[E] {Label}(으)로 이동{suffix}");
    }

    bool InRange(PlayerAvatar avatar)
    {
        return Vector3.Distance(avatar.transform.position, transform.position) <= InteractRange;
    }

    bool MeetsRequirement(PlayerAvatar avatar)
    {
        if (RequiredLevel <= 0)
        {
            return true;
        }

        var stats = avatar.GetComponent<PlayerStats>();
        return stats == null || stats.Level.Value >= RequiredLevel;
    }
}
