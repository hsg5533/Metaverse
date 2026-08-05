using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

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
        ("Sharpen weapon  (3 Ore, 1 Wood)", 3, 1, true),
        ("Reinforce armor  (2 Ore, 2 Wood)", 2, 2, false),
    };

    /// <summary>Cooking recipes: materials in, a timed buff out.</summary>
    public static readonly (string Name, int Ore, int Herb, int Wood, int Buff, float Seconds)[] CookRecipes =
    {
        ("Herb stew  (2 Herb, 1 Wood)  +6 ATK 3m", 0, 2, 1, PlayerBuffs.Attack, 180f),
        ("Iron broth  (1 Ore, 1 Herb, 1 Wood)  +6 DEF 3m", 1, 1, 1, PlayerBuffs.Defense, 180f),
        ("Traveller's tea  (2 Herb)  +40% speed 2m", 0, 2, 0, PlayerBuffs.Speed, 120f),
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
            NoticeRpc(NetText.Trim64("Not enough materials."));
            return;
        }

        if (entry.Weapon)
        {
            stats.WeaponLevel.Value++;
            NoticeRpc(NetText.Trim64($"Weapon Lv.{stats.WeaponLevel.Value}!  ATK {stats.AttackPower}"));
        }
        else
        {
            stats.ArmorLevel.Value++;
            NoticeRpc(NetText.Trim64($"Armor Lv.{stats.ArmorLevel.Value}!  DEF {stats.Defense}"));
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
            NoticeRpc(NetText.Trim64("Not enough materials."));
            return;
        }

        buffs.Apply(entry.Buff, entry.Seconds);
        NoticeRpc(NetText.Trim64($"Cooked. {PlayerBuffs.NameOf(entry.Buff)} for {Mathf.RoundToInt(entry.Seconds / 60f)}m."));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString64Bytes text)
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


    void OnGUI()
    {
        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(Screen.width - 230, 162, 220, 26), GUI.skin.box);
        GUILayout.Label($"Ore {Ore.Value}   Herb {Herb.Value}   Wood {Wood.Value}");
        GUILayout.EndArea();
    }
}
