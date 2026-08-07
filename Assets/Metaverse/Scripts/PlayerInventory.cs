using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>What a gathering node hands out, and what the stations ask for.</summary>
public enum GatherKind
{
    Ore = 0,
    Herb = 1,
    Wood = 2,
}

/// <summary>
/// The materials a player carries, plus the crafting and cooking the stations trigger.
/// The server owns every count; stations only ask.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerInventory : NetworkBehaviour
{
    /// <summary>Crafting recipes: gear upgrades paid in materials instead of gold.</summary>
    public static readonly (string Name, int Ore, int Wood, bool Weapon)[] CraftRecipes =
    {
        ("검 강화  (광석 3, 나무 1)", 3, 1, true),
        ("방어구 강화  (광석 2, 나무 2)", 2, 2, false),
    };

    /// <summary>Cooking recipes: materials in, a timed buff out.</summary>
    public static readonly (string Name, int Ore, int Herb, int Wood, int Buff, float Seconds)[] CookRecipes =
    {
        ("약초 스튜  (약초 2, 나무 1)  공격 +6 · 3분", 0, 2, 1, PlayerBuffs.Attack, 180f),
        ("철분 수프  (광석 1, 약초 1, 나무 1)  방어 +6 · 3분", 1, 1, 1, PlayerBuffs.Defense, 180f),
        ("여행자의 차  (약초 2)  이동속도 +40% · 2분", 0, 2, 0, PlayerBuffs.Speed, 120f),
    };

    /// <summary>True while the local player has the bag open; other panels stay shut.</summary>
    public static bool WindowOpen;

    /// <summary>What each material slot holds, in the same order as the preview models.</summary>
    public static readonly string[] Slots = { "광석", "약초", "나무" };

    /// <summary>Which stack a saved name refers to, or -1 when it is not a material.</summary>
    public static int IndexOf(string name)
    {
        return System.Array.IndexOf(Slots, name);
    }

    public NetworkVariable<int> Ore = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Herb = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Wood = new(0, writePerm: NetworkVariableWritePermission.Server);


    PlayerStats stats;
    PlayerBuffs buffs;
    PlayerGear gear;

    /// <summary>Which material stacks are actually carried, so empty ones take no slot.</summary>
    readonly int[] heldMaterials = new int[3];
    int heldCount;
    Vector2 bagScroll;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        buffs = GetComponent<PlayerBuffs>();
        gear = GetComponent<PlayerGear>();
    }

    /// <summary>Server side.</summary>
    public void Add(GatherKind kind, int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        switch (kind)
        {
            case GatherKind.Ore: Ore.Value += amount; break;
            case GatherKind.Herb: Herb.Value += amount; break;
            default: Wood.Value += amount; break;
        }
    }

    /// <summary>Server side: takes the materials only if all of them are there.</summary>
    public bool Spend(int ore, int herb, int wood)
    {
        if (!IsServer || Ore.Value < ore || Herb.Value < herb || Wood.Value < wood)
        {
            return false;
        }

        Ore.Value -= ore;
        Herb.Value -= herb;
        Wood.Value -= wood;
        return true;
    }

    [Rpc(SendTo.Server)]
    public void CraftRpc(int recipe, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || recipe < 0 || recipe >= CraftRecipes.Length)
        {
            return;
        }

        var entry = CraftRecipes[recipe];
        if (!Spend(entry.Ore, 0, entry.Wood))
        {
            NoticeRpc(NetText.Trim512("재료가 부족합니다."));
            return;
        }

        if (entry.Weapon)
        {
            stats.WeaponLevel.Value++;
            NoticeRpc(NetText.Trim512($"검 Lv.{stats.WeaponLevel.Value}! 공격력 {stats.AttackPower}"));
        }
        else
        {
            stats.ArmorLevel.Value++;
            NoticeRpc(NetText.Trim512($"방어구 Lv.{stats.ArmorLevel.Value}! 방어력 {stats.Defense}"));
        }
    }

    [Rpc(SendTo.Server)]
    public void CookRpc(int recipe, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || recipe < 0 || recipe >= CookRecipes.Length)
        {
            return;
        }

        var entry = CookRecipes[recipe];
        if (!Spend(entry.Ore, entry.Herb, entry.Wood))
        {
            NoticeRpc(NetText.Trim512("재료가 부족합니다."));
            return;
        }

        buffs.Apply(entry.Buff, entry.Seconds);
        NoticeRpc(NetText.Trim512($"요리 완성. {PlayerBuffs.NameOf(entry.Buff)} {Mathf.RoundToInt(entry.Seconds / 60f)}분."));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Local(text.ToString());
    }

    /// <summary>What the shop pays for one of each material, in Slots order.</summary>
    public static readonly int[] MaterialPrices = { 12, 8, 6 };

    /// <summary>Sell one stack to the shopkeeper.</summary>
    [Rpc(SendTo.Server)]
    public void SellRpc(int material, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || material < 0 || material >= Slots.Length)
        {
            return;
        }

        int count = CountOf(material);
        if (count <= 0)
        {
            return;
        }

        Spend(material == 0 ? count : 0, material == 1 ? count : 0, material == 2 ? count : 0);
        stats.Gold.Value += count * MaterialPrices[material];
        NoticeRpc(NetText.Trim512($"{Slots[material]} {count}개를 {count * MaterialPrices[material]} 골드에 팔았습니다."));
    }

    /// <summary>Server side: used by the save file.</summary>
    public void SetAll(int ore, int herb, int wood)
    {
        if (!IsServer)
        {
            return;
        }

        Ore.Value = Mathf.Max(0, ore);
        Herb.Value = Mathf.Max(0, herb);
        Wood.Value = Mathf.Max(0, wood);
    }


    public override void OnNetworkDespawn()
    {
        if (IsOwner && WindowOpen)
        {
            WindowOpen = false;
            ShopNpc.PanelOpen = false;
        }
    }

    void Update()
    {
        if (!IsOwner || !IsSpawned || ChatSystem.IsTyping)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        // The bag cannot be opened on top of a shop, and Escape always closes it.
        if ((keyboard.iKey.wasPressedThisFrame || MobileInput.Pressed(Key.I)) && (WindowOpen || !ShopNpc.PanelOpen))
        {
            SetWindow(!WindowOpen);
        }
        else if (WindowOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetWindow(false);
        }
    }

    void SetWindow(bool open)
    {
        WindowOpen = open;
        ShopNpc.PanelOpen = open;
    }

    public int CountOf(int slot)
    {
        return slot switch
        {
            0 => Ore.Value,
            1 => Herb.Value,
            _ => Wood.Value,
        };
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsOwner || !IsSpawned || !WindowOpen)
        {
            return;
        }

        const float slotSize = 64f;
        const float padding = 8f;
        const int columns = 5;
        const int rows = 3;
        const float gearWidth = 192f;

        float gridWidth = columns * (slotSize + padding) + padding;
        float width = gearWidth + gridWidth;
        float height = Mathf.Max(rows * (slotSize + padding) + padding + 96f, 300f);
        var window = new Rect(MetaverseUi.Width * 0.5f - width * 0.5f, MetaverseUi.Height * 0.5f - height * 0.5f, width, height);

        GUI.Box(window, "");
        GUI.Label(new Rect(window.x + padding, window.y + 6f, width, 22f), "<b>인벤토리</b>   [I] 닫기", MetaverseUi.Rich);

        if (stats != null)
        {
            GUI.Label(new Rect(window.x + padding, window.y + 28f, width, 22f),
                $"골드 {stats.Gold.Value}     공격력 {stats.AttackPower}     방어력 {stats.Defense}");
        }

        float contentTop = window.y + 54f;
        DrawEquipment(new Rect(window.x + padding, contentTop, gearWidth - padding * 2f, height - 62f), stats);

        // One bag: the stacks that are actually held come first, then the gear pieces.
        heldCount = 0;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (CountOf(i) > 0)
            {
                heldMaterials[heldCount++] = i;
            }
        }

        // Three rows are on show and the rest is scrolled to: the bag itself has no limit.
        int carried = heldCount + (gear != null ? gear.Bag.Count : 0);
        int contentRows = Mathf.Max(rows, Mathf.CeilToInt(carried / (float)columns));

        var view = new Rect(window.x + gearWidth, contentTop, gridWidth, rows * (slotSize + padding));
        var content = new Rect(0f, 0f, gridWidth - 20f, contentRows * (slotSize + padding));

        bagScroll = GUI.BeginScrollView(view, bagScroll, content);
        for (int index = 0; index < contentRows * columns; index++)
        {
            var slot = new Rect(
                (index % columns) * (slotSize + padding),
                (index / columns) * (slotSize + padding),
                slotSize, slotSize);

            DrawSlot(slot, index);
        }
        GUI.EndScrollView();

        GUI.Label(new Rect(window.x + gearWidth, window.y + height - 26f, gridWidth, 22f),
            "가방의 장비를 누르면 착용, 장비 칸을 누르면 벗는다.");
    }

    /// <summary>
    /// The gear panel: what is worn right now. Weapons that are owned but not in hand sit in
    /// the bag next door, and clicking one there swaps it into this slot.
    /// </summary>
    void DrawEquipment(Rect area, PlayerStats stats)
    {
        GUI.Label(new Rect(area.x, area.y - 2f, area.width, 20f), "<b>장비</b>", MetaverseUi.Rich);

        if (stats == null)
        {
            return;
        }

        DrawGearSlot(new Rect(area.x, area.y + 20f, area.width, 66f), "무기", stats.WeaponName,
            $"+{stats.WeaponBonus} ATK", GearPreview.Weapon);

        DrawGearSlot(new Rect(area.x, area.y + 94f, area.width, 66f), "방어구", stats.ArmorName,
            $"+{stats.ArmorBonus} DEF", GearPreview.Armor);

        GUI.Label(new Rect(area.x, area.y + 170f, area.width, 60f),
            "상점과 모루에서 강화하고,\n몬스터에게서 얻는다.");
    }

    void DrawGearSlot(Rect slot, string label, string name, string bonus, int preview)
    {
        var icon = new Rect(slot.x, slot.y, 60f, 60f);
        DrawSlotBackground(icon);

        // The real models, turning slowly, rendered by GearPreview.
        GearPreview.Draw(icon, preview);

        // Clicking what is worn takes it off; the plain sword comes back and the piece
        // returns to the bag.
        if (GUI.Button(icon, GUIContent.none, GUIStyle.none) && gear != null)
        {
            gear.UnequipRpc(preview == GearPreview.Weapon);
        }

        GUI.Label(new Rect(slot.x + 66f, slot.y + 4f, slot.width - 66f, 18f), label);
        GUI.Label(new Rect(slot.x + 66f, slot.y + 22f, slot.width - 66f, 18f), name);
        GUI.Label(new Rect(slot.x + 66f, slot.y + 40f, slot.width - 66f, 18f), bonus);
    }

    /// <summary>The sunken square every slot sits in.</summary>
    static void DrawSlotBackground(Rect slot)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(slot, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Box(slot, GUIContent.none);
    }

    /// <summary>A material stack, or a piece of dropped gear waiting to be worn.</summary>
    void DrawSlot(Rect slot, int index)
    {
        DrawSlotBackground(slot);

        if (index < heldCount)
        {
            DrawMaterialSlot(slot, heldMaterials[index]);
            return;
        }

        DrawBagSlot(slot, index - heldCount);
    }

    /// <summary>One stack of ore, herb or wood, with how many are held.</summary>
    void DrawMaterialSlot(Rect slot, int material)
    {
        // Materials are modelled the same way the gear is: slot 0 and 1 are the weapon and
        // the armour, so the material models start after them.
        GearPreview.Draw(new Rect(slot.x + 6f, slot.y + 2f, slot.width - 12f, slot.height - 20f), GearPreview.Ore + material);

        GUI.Label(new Rect(slot.x + 4f, slot.y + slot.height - 20f, slot.width - 8f, 18f), Slots[material]);
        GUI.Label(new Rect(slot.x, slot.y + slot.height - 20f, slot.width - 6f, 18f), CountOf(material).ToString(), RightAligned());
    }

    /// <summary>One dropped piece; clicking the slot puts it on.</summary>
    void DrawBagSlot(Rect slot, int bagIndex)
    {
        if (gear == null || bagIndex >= gear.Bag.Count)
        {
            return;
        }

        int piece = gear.Bag[bagIndex];
        GearPreview.Draw(new Rect(slot.x + 4f, slot.y, slot.width - 8f, slot.height - 18f), GearPreview.Piece + piece);
        GUI.Label(new Rect(slot.x + 4f, slot.y + slot.height - 20f, slot.width - 8f, 18f), PlayerGear.Pieces[piece].Name);

        if (GUI.Button(slot, GUIContent.none, GUIStyle.none))
        {
            gear.EquipRpc(bagIndex);
        }
    }

    static GUIStyle rightAligned;

    static GUIStyle RightAligned()
    {
        rightAligned ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        return rightAligned;
    }
}
