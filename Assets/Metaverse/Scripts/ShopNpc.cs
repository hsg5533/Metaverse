using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Village shopkeeper. Purely local UI: it shows a prompt when the local avatar is close
/// and sends the purchase to the server through <see cref="PlayerStats"/> or <see cref="PlayerGear"/>.
/// </summary>
public class ShopNpc : MonoBehaviour
{
    /// <summary>True while any shop window is open, so the attack input stays quiet.</summary>
    public static bool PanelOpen;

    static readonly string[] Tabs = { "구매", "강화", "판매" };

    public string ShopName = "마을 상점";
    public float InteractRange = 3.5f;
    public float PromptHeight = 2.4f;

    bool open;
    int tab;
    Vector2 buyScroll;
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
    /// The rod, the shop's own weapons and armour, and every material - none of it dropped by
    /// anything - drawn the same way the bag shows what is already owned: a real 3D model
    /// turning slowly, cloned by <see cref="GearPreview"/>.
    /// </summary>
    void DrawBuying(Rect area, PlayerStats stats, PlayerGear gear)
    {
        const float rowHeight = 70f;
        var inventory = stats.GetComponent<PlayerInventory>();
        int rows = 1 + PlayerGear.ShopCount + PlayerInventory.Slots.Length;
        var content = new Rect(0f, 0f, area.width - 20f, rows * rowHeight);

        buyScroll = GUI.BeginScrollView(area, buyScroll, content);
        float y = 0f;

        MetaverseUi.ItemRow(new Rect(0f, y, content.width, rowHeight - 6f), GearPreview.Piece + PlayerGear.Rod,
            "낚싯대", "호수에서 쓴다", $"구매  ({PlayerGear.RodPrice} 골드)", () => gear.BuyRodRpc());
        y += rowHeight;

        for (int i = 0; i < PlayerGear.ShopCount; i++)
        {
            int piece = PlayerGear.ShopFirst + i;
            PlayerGear.Piece info = PlayerGear.Pieces[piece];
            string bonus = info.Weapon ? $"공격 +{info.Bonus}" : $"방어 +{info.Bonus}";
            int price = PlayerGear.BuyPriceOf(piece);

            MetaverseUi.ItemRow(new Rect(0f, y, content.width, rowHeight - 6f), GearPreview.Piece + piece,
                info.Name, bonus, $"구매  ({price} 골드)", () => gear.BuyPieceRpc(piece));
            y += rowHeight;
        }

        for (int i = 0; i < PlayerInventory.Slots.Length; i++)
        {
            int material = i;
            int price = PlayerInventory.MaterialBuyPrices[material];

            MetaverseUi.ItemRow(new Rect(0f, y, content.width, rowHeight - 6f), GearPreview.Ore + material,
                PlayerInventory.Slots[material], $"보유 {inventory.CountOf(material)}개",
                $"구매  ({price} 골드)", () => inventory.BuyMaterialRpc(material));
            y += rowHeight;
        }

        GUI.EndScrollView();
    }

    /// <summary>
    /// The old generic level-up path, now with the same 3D preview as everything else: the
    /// bare sword and, when nothing fancier is worn, the plain armour <see cref="GearPreview"/>
    /// falls back to.
    /// </summary>
    void DrawUpgrading(Rect area, PlayerStats stats, PlayerGear gear)
    {
        MetaverseUi.ItemRow(new Rect(area.x, area.y, area.width, 66f), GearPreview.Weapon,
            $"검 Lv.{stats.WeaponLevel.Value}", $"공격 +{stats.WeaponBonus}",
            $"Lv.{stats.WeaponLevel.Value + 1}로 강화  ({stats.WeaponPrice} 골드)", () => stats.BuyWeaponRpc());

        MetaverseUi.ItemRow(new Rect(area.x, area.y + 74f, area.width, 66f), GearPreview.Armor,
            $"방어구 Lv.{stats.ArmorLevel.Value}", $"방어 +{stats.ArmorBonus}",
            $"Lv.{stats.ArmorLevel.Value + 1}로 강화  ({stats.ArmorPrice} 골드)", () => stats.BuyArmorRpc());
    }

    /// <summary>
    /// Everything in the bag, one row each with the same 3D preview as the buy list, behind a
    /// scroll bar: a full bag is fifteen rows and the window is not going to grow to fit them.
    /// A dish is not sellable here - the bag is where it gets eaten instead.
    /// </summary>
    void DrawSelling(Rect area, PlayerStats stats, PlayerGear gear)
    {
        const float rowHeight = 70f;

        // The player prefab always carries all three; the builder adds them together.
        var inventory = stats.GetComponent<PlayerInventory>();

        // An upper bound, not an exact count: empty stacks and dishes skip their row below,
        // which just leaves a little unused scroll space instead of a second pass to count them.
        int rows = PlayerInventory.Slots.Length + gear.Bag.Count;
        var content = new Rect(0f, 0f, area.width - 20f, Mathf.Max(rows, 1) * rowHeight);
        sellScroll = GUI.BeginScrollView(area, sellScroll, content);
        float y = 0f;

        for (int i = 0; i < PlayerInventory.Slots.Length; i++)
        {
            int material = i;
            int count = inventory.CountOf(material);
            if (count <= 0)
            {
                continue;
            }

            int paid = count * PlayerInventory.MaterialPrices[material];
            MetaverseUi.ItemRow(new Rect(0f, y, content.width, rowHeight - 6f), GearPreview.Ore + material,
                PlayerInventory.Slots[material], $"보유 {count}개",
                $"판매  ({paid} 골드)", () => inventory.SellRpc(material));
            y += rowHeight;
        }

        for (int i = 0; i < gear.Bag.Count; i++)
        {
            int piece = gear.Bag[i];
            if (piece < 0 || PlayerGear.Pieces[piece].IsFood)
            {
                continue;
            }

            int bagIndex = i;
            PlayerGear.Piece info = PlayerGear.Pieces[piece];
            string bonus = info.Weapon ? $"공격 +{info.Bonus}" : $"방어 +{info.Bonus}";
            int price = PlayerGear.PriceOf(piece);

            MetaverseUi.ItemRow(new Rect(0f, y, content.width, rowHeight - 6f), GearPreview.Piece + piece,
                info.Name, bonus, $"판매  ({price} 골드)", () => gear.SellRpc(bagIndex));
            y += rowHeight;
        }

        GUI.EndScrollView();
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
        var gear = stats.GetComponent<PlayerGear>();
        var window = new Rect(MetaverseUi.Width * 0.5f - 200f, MetaverseUi.Height * 0.5f - 200f, 400f, 400f);

        GUI.Box(window, "");
        GUI.Label(new Rect(window.x + 10f, window.y + 6f, window.width - 20f, 22f),
            $"<b>{ShopName}</b>   골드 {stats.Gold.Value}", MetaverseUi.Rich);

        tab = GUI.Toolbar(new Rect(window.x + 10f, window.y + 30f, window.width - 20f, 24f), tab, Tabs);

        var content = new Rect(window.x + 10f, window.y + 60f, window.width - 20f, window.height - 128f);
        switch (tab)
        {
            case 0:
                DrawBuying(content, stats, gear);
                break;
            case 1:
                DrawUpgrading(content, stats, gear);
                break;
            default:
                DrawSelling(content, stats, gear);
                break;
        }

        GUI.Label(new Rect(window.x + 10f, window.y + window.height - 58f, window.width - 20f, 22f),
            $"체력 {stats.Hp.Value}/{stats.MaxHp}   공격 {stats.AttackPower}   방어 {stats.Defense}");

        if (GUI.Button(new Rect(window.x + 10f, window.y + window.height - 32f, window.width - 20f, 24f), "닫기  [E]"))
        {
            Close();
        }
    }
}
