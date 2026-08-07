using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Village shopkeeper. Purely local UI: it shows a prompt when the local avatar is close
/// and sends the purchase to the server through <see cref="PlayerStats"/>.
/// </summary>
public class ShopNpc : MonoBehaviour
{
    /// <summary>True while any shop window is open, so the attack input stays quiet.</summary>
    public static bool PanelOpen;

    public string ShopName = "마을 상점";
    public float InteractRange = 3.5f;
    public float PromptHeight = 2.4f;

    bool open;

    void OnDisable()
    {
        Close();
    }

    void Update()
    {
        var stats = PlayerAvatar.Local != null ? PlayerAvatar.Local.GetComponent<PlayerStats>() : null;
        if (stats == null || Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) > InteractRange)
        {
            Close();
            return;
        }

        if (MetaverseUi.InteractPressed && !ChatSystem.IsTyping && !PlayerInventory.WindowOpen)
        {
            open = !open;
            PanelOpen = open;
        }
    }

    void Close()
    {
        if (open)
        {
            open = false;
            PanelOpen = false;
        }
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        var stats = PlayerAvatar.Local != null ? PlayerAvatar.Local.GetComponent<PlayerStats>() : null;
        if (stats == null)
        {
            return;
        }

        if (open)
        {
            DrawShop(stats);
            return;
        }

        if (Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) > InteractRange)
        {
            return;
        }

        MetaverseUi.WorldPrompt(transform.position + Vector3.up * PromptHeight, $"[E] {ShopName}");
    }

    void DrawShop(PlayerStats stats)
    {
        var area = new Rect(MetaverseUi.Width * 0.5f - 190f, MetaverseUi.Height * 0.5f - 140f, 380f, 280f);
        GUILayout.BeginArea(area, GUI.skin.box);

        GUILayout.Label($"<b>{ShopName}</b>", MetaverseUi.Rich);
        GUILayout.Label($"골드: {stats.Gold.Value}");
        GUILayout.Space(6);

        if (GUILayout.Button($"검 Lv.{stats.WeaponLevel.Value} → Lv.{stats.WeaponLevel.Value + 1}  (골드 {stats.WeaponPrice})  공격 +4"))
        {
            stats.BuyWeaponRpc();
        }

        if (GUILayout.Button($"방어구 Lv.{stats.ArmorLevel.Value} → Lv.{stats.ArmorLevel.Value + 1}  (골드 {stats.ArmorPrice})  방어 +3"))
        {
            stats.BuyArmorRpc();
        }

        GUILayout.Space(6);
        GUILayout.Label($"체력 {stats.Hp.Value}/{stats.MaxHp}   공격 {stats.AttackPower}   방어 {stats.Defense}");

        if (GUILayout.Button("닫기  [E]"))
        {
            Close();
        }

        GUILayout.EndArea();
    }
}
