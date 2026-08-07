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
    Vector2 sellScroll;

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

    /// <summary>
    /// Everything in the bag, one button each, behind a scroll bar: a full bag is fifteen
    /// rows and the window is not going to grow to fit them.
    /// </summary>
    void DrawSelling(PlayerStats stats)
    {
        sellScroll = GUILayout.BeginScrollView(sellScroll, GUILayout.Height(120f));

        var inventory = stats.GetComponent<PlayerInventory>();
        for (int i = 0; inventory != null && i < PlayerInventory.Slots.Length; i++)
        {
            int count = inventory.CountOf(i);
            if (count > 0 && GUILayout.Button($"{PlayerInventory.Slots[i]} {count}개  →  {count * PlayerInventory.MaterialPrices[i]} 골드"))
            {
                inventory.SellRpc(i);
            }
        }

        var gear = stats.GetComponent<PlayerGear>();
        for (int i = 0; gear != null && i < gear.Bag.Count; i++)
        {
            int piece = gear.Bag[i];
            if (GUILayout.Button($"{PlayerGear.Pieces[piece].Name}  →  {PlayerGear.PriceOf(piece)} 골드"))
            {
                gear.SellRpc(i);
                break;
            }
        }

        GUILayout.EndScrollView();
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
        var area = new Rect(MetaverseUi.Width * 0.5f - 190f, MetaverseUi.Height * 0.5f - 200f, 380f, 400f);
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

        GUILayout.Space(8);
        GUILayout.Label("<b>판매</b>", MetaverseUi.Rich);
        DrawSelling(stats);

        GUILayout.Space(6);
        GUILayout.Label($"체력 {stats.Hp.Value}/{stats.MaxHp}   공격 {stats.AttackPower}   방어 {stats.Defense}");

        if (GUILayout.Button("닫기  [E]"))
        {
            Close();
        }

        GUILayout.EndArea();
    }
}
