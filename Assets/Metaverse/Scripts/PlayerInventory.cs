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

    /// <summary>What each slot holds and how it is drawn.</summary>
    static readonly (string Name, Color Colour)[] Slots =
    {
        ("광석", new Color(0.42f, 0.82f, 0.92f)),
        ("약초", new Color(0.40f, 0.78f, 0.42f)),
        ("나무", new Color(0.54f, 0.38f, 0.24f)),
    };

    public NetworkVariable<int> Ore = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Herb = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Wood = new(0, writePerm: NetworkVariableWritePermission.Server);


    PlayerStats stats;
    PlayerBuffs buffs;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        buffs = GetComponent<PlayerBuffs>();
    }

    public int Count(GatherKind kind)
    {
        return kind switch
        {
            GatherKind.Ore => Ore.Value,
            GatherKind.Herb => Herb.Value,
            _ => Wood.Value,
        };
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

    /// <summary>Server side: used by the save file and by trading.</summary>
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
        if (keyboard.iKey.wasPressedThisFrame && (WindowOpen || !ShopNpc.PanelOpen))
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

    int CountOf(int slot)
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
        var window = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.5f - height * 0.5f, width, height);

        GUI.Box(window, "");
        GUI.Label(new Rect(window.x + padding, window.y + 6f, width, 22f), "<b>인벤토리</b>   [I] 닫기", RichLabel());

        var stats = GetComponent<PlayerStats>();
        if (stats != null)
        {
            GUI.Label(new Rect(window.x + padding, window.y + 28f, width, 22f),
                $"골드 {stats.Gold.Value}     공격력 {stats.AttackPower}     방어력 {stats.Defense}");
        }

        float contentTop = window.y + 54f;
        DrawEquipment(new Rect(window.x + padding, contentTop, gearWidth - padding * 2f, height - 62f), stats);

        float gridLeft = window.x + gearWidth;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                var slot = new Rect(
                    gridLeft + column * (slotSize + padding),
                    contentTop + row * (slotSize + padding),
                    slotSize, slotSize);

                DrawSlot(slot, index);
            }
        }

        GUI.Label(new Rect(gridLeft, window.y + height - 26f, gridWidth, 22f),
            "모루는 광석과 나무로 장비를, 모닥불은 약초로 요리를 만든다.");
    }

    /// <summary>
    /// The gear panel: what is worn right now. Weapons that are owned but not in hand sit in
    /// the bag next door, and clicking one there swaps it into this slot.
    /// </summary>
    void DrawEquipment(Rect area, PlayerStats stats)
    {
        GUI.Label(new Rect(area.x, area.y - 2f, area.width, 20f), "<b>장비</b>", RichLabel());

        if (stats == null)
        {
            return;
        }

        DrawGearSlot(new Rect(area.x, area.y + 20f, area.width, 66f), "무기", stats.WeaponName,
            $"+{stats.WeaponBonus} ATK", new Color(0.78f, 0.80f, 0.86f), true);

        DrawGearSlot(new Rect(area.x, area.y + 94f, area.width, 66f), "방어구", stats.ArmorName,
            $"+{stats.ArmorBonus} DEF", new Color(0.72f, 0.55f, 0.32f), false);

        GUI.Label(new Rect(area.x, area.y + 170f, area.width, 60f),
            "상점이나 모루에서\n강화한다.");
    }

    static void DrawGearSlot(Rect slot, string label, string name, string bonus, Color colour, bool blade)
    {
        var icon = new Rect(slot.x, slot.y, 60f, 60f);
        DrawSlotBackground(icon);

        Color previous = GUI.color;
        GUI.color = colour;

        if (blade)
        {
            // Blade and crossguard.
            GUI.DrawTexture(new Rect(icon.center.x - 3f, icon.y + 8f, 6f, 34f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(icon.center.x - 12f, icon.y + 40f, 24f, 4f), Texture2D.whiteTexture);
        }
        else
        {
            // Chest piece with two shoulders.
            GUI.DrawTexture(new Rect(icon.center.x - 16f, icon.y + 18f, 32f, 28f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(icon.center.x - 22f, icon.y + 14f, 12f, 12f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(icon.center.x + 10f, icon.y + 14f, 12f, 12f), Texture2D.whiteTexture);
        }

        GUI.color = previous;

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

    /// <summary>One material stack: a coloured block, its name and how many are held.</summary>
    void DrawSlot(Rect slot, int index)
    {
        DrawSlotBackground(slot);

        if (index >= Slots.Length)
        {
            return;
        }

        int count = CountOf(index);
        if (count <= 0)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = Slots[index].Colour;
        GUI.DrawTexture(new Rect(slot.x + 14f, slot.y + 10f, slot.width - 28f, slot.height - 30f), Texture2D.whiteTexture);
        GUI.color = previous;

        GUI.Label(new Rect(slot.x + 4f, slot.y + slot.height - 20f, slot.width - 8f, 18f), Slots[index].Name);
        GUI.Label(new Rect(slot.x, slot.y + slot.height - 20f, slot.width - 6f, 18f), count.ToString(), RightAligned());
    }

    static GUIStyle richLabel;
    static GUIStyle rightAligned;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }

    static GUIStyle RightAligned()
    {
        rightAligned ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
        return rightAligned;
    }
}
