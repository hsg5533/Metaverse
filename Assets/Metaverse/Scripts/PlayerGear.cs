using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gear that drops off monsters: what is carried, what is worn, and which model shows on the
/// body. The server owns both, so every client sees the same sword in the same hand.
/// </summary>
public class PlayerGear : NetworkBehaviour
{
    public readonly struct Piece
    {
        public readonly string Name;
        public readonly bool Weapon;
        public readonly int Bonus;

        /// <summary>The ground it drops on, which is also the model to show.</summary>
        public readonly int Theme;

        public Piece(string name, bool weapon, int bonus, int theme)
        {
            Name = name;
            Weapon = weapon;
            Bonus = bonus;
            Theme = theme;
        }
    }

    /// <summary>A weapon and an armour per ground, in the same theme order the monsters use.</summary>
    public static readonly Piece[] Pieces =
    {
        new("강철 검", true, 6, 0),
        new("강철 갑옷", false, 5, 0),
        new("서리 검", true, 12, 1),
        new("서리 갑옷", false, 10, 1),
        new("용암 검", true, 20, 2),
        new("용암 갑옷", false, 16, 2),
    };

    /// <summary>The bare sword first, then one weapon per theme.</summary>
    public GameObject[] WeaponModels;

    /// <summary>One armour per theme; nothing worn means none of them show.</summary>
    public GameObject[] ArmorModels;

    /// <summary>The shoulder pieces, hung off the arms so they move with the swing.</summary>
    public GameObject[] ArmorLeftArmModels;
    public GameObject[] ArmorRightArmModels;

    public NetworkVariable<int> Weapon = new(-1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Armor = new(-1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkList<int> Bag = new();

    public int AttackBonus => Held(Weapon)?.Bonus ?? 0;
    public int DefenseBonus => Held(Armor)?.Bonus ?? 0;
    public string WeaponName => Held(Weapon)?.Name;
    public string ArmorName => Held(Armor)?.Name;

    /// <summary>What a piece is called, or an empty string when nothing is worn.</summary>
    public static string NameOf(int piece)
    {
        return Valid(piece) ? Pieces[piece].Name : "";
    }

    /// <summary>What the shop pays for a piece: worth about two levels of the upgrade it saves.</summary>
    public static int PriceOf(int piece) => Pieces[piece].Bonus * 12;

    /// <summary>Which piece a saved name refers to, or -1 when it is not gear.</summary>
    public static int IndexOf(string name)
    {
        for (int i = 0; i < Pieces.Length; i++)
        {
            if (Pieces[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The piece index a monster of this ground drops.</summary>
    public static int PieceFor(int theme, bool weapon)
    {
        return Mathf.Clamp(theme, 0, Pieces.Length / 2 - 1) * 2 + (weapon ? 0 : 1);
    }

    public override void OnNetworkSpawn()
    {
        Weapon.OnValueChanged += OnGearChanged;
        Armor.OnValueChanged += OnGearChanged;
        ApplyModels();
    }

    public override void OnNetworkDespawn()
    {
        Weapon.OnValueChanged -= OnGearChanged;
        Armor.OnValueChanged -= OnGearChanged;
    }

    void OnGearChanged(int previous, int current)
    {
        ApplyModels();
    }

    /// <summary>Server side: hand over a drop.</summary>
    public void Give(int piece)
    {
        if (!IsServer || piece < 0 || piece >= Pieces.Length)
        {
            return;
        }

        // ponytail: no cap. The bag scrolls, and a runaway could only come from a bug.
        Bag.Add(piece);
        NoticeRpc(NetText.Trim512($"{Pieces[piece].Name}을(를) 획득했습니다."));
    }

    /// <summary>Server side: used by the save file.</summary>
    public void Restore(int weapon, int armor, List<int> bag)
    {
        if (!IsServer)
        {
            return;
        }

        Weapon.Value = Valid(weapon) ? weapon : -1;
        Armor.Value = Valid(armor) ? armor : -1;

        Bag.Clear();
        if (bag != null)
        {
            foreach (int piece in bag)
            {
                if (Valid(piece))
                {
                    Bag.Add(piece);
                }
            }
        }
    }

    /// <summary>Wear a bag item; whatever came off goes back into the bag.</summary>
    [Rpc(SendTo.Server)]
    public void EquipRpc(int bagIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || bagIndex < 0 || bagIndex >= Bag.Count)
        {
            return;
        }

        int piece = Bag[bagIndex];
        var slot = Pieces[piece].Weapon ? Weapon : Armor;
        int previous = slot.Value;

        slot.Value = piece;
        Bag.RemoveAt(bagIndex);

        if (previous >= 0)
        {
            Bag.Add(previous);
        }
    }

    /// <summary>Take a piece off: it goes back to the bag and the plain gear comes back.</summary>
    [Rpc(SendTo.Server)]
    public void UnequipRpc(bool weapon, RpcParams rpcParams = default)
    {
        var slot = weapon ? Weapon : Armor;
        if (rpcParams.Receive.SenderClientId != OwnerClientId || slot.Value < 0)
        {
            return;
        }

        Bag.Add(slot.Value);
        slot.Value = -1;
    }

    /// <summary>Sell a bag item to the shopkeeper. Worn gear has to come off first.</summary>
    [Rpc(SendTo.Server)]
    public void SellRpc(int bagIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || bagIndex < 0 || bagIndex >= Bag.Count)
        {
            return;
        }

        int piece = Bag[bagIndex];
        Bag.RemoveAt(bagIndex);

        var stats = GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.Gold.Value += PriceOf(piece);
        }

        NoticeRpc(NetText.Trim512($"{Pieces[piece].Name}을(를) {PriceOf(piece)} 골드에 팔았습니다."));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Local(text.ToString());
        GameSound.PlayLocal(GameSound.Pickup);
    }

    /// <summary>
    /// Every object a piece is made of: a sword is one, a suit of armour is three - the chest
    /// and the two shoulders that hang off the arms. The icons clone exactly this, so what is
    /// in the window is what is on the body.
    /// </summary>
    public GameObject[] PartsFor(int piece)
    {
        if (!Valid(piece))
        {
            return System.Array.Empty<GameObject>();
        }

        Piece entry = Pieces[piece];
        return entry.Weapon
            ? new[] { At(WeaponModels, entry.Theme + 1) }
            : new[] { At(ArmorModels, entry.Theme), At(ArmorLeftArmModels, entry.Theme), At(ArmorRightArmModels, entry.Theme) };
    }

    /// <summary>The same, for what is worn: the bare sword when no weapon is on.</summary>
    public GameObject[] EquippedParts(bool weapon)
    {
        int piece = (weapon ? Weapon : Armor).Value;
        if (Valid(piece))
        {
            return PartsFor(piece);
        }

        return weapon ? new[] { At(WeaponModels, 0) } : System.Array.Empty<GameObject>();
    }

    void ApplyModels()
    {
        Show(WeaponModels, Held(Weapon)?.Theme + 1 ?? 0);

        int armor = Held(Armor)?.Theme ?? -1;
        Show(ArmorModels, armor);
        Show(ArmorLeftArmModels, armor);
        Show(ArmorRightArmModels, armor);
    }

    static void Show(GameObject[] models, int index)
    {
        if (models == null)
        {
            return;
        }

        for (int i = 0; i < models.Length; i++)
        {
            if (models[i] != null)
            {
                models[i].SetActive(i == index);
            }
        }
    }

    static GameObject At(GameObject[] models, int index)
    {
        return models != null && index >= 0 && index < models.Length ? models[index] : null;
    }

    static bool Valid(int piece)
    {
        return piece >= 0 && piece < Pieces.Length;
    }

    Piece? Held(NetworkVariable<int> slot)
    {
        return Valid(slot.Value) ? Pieces[slot.Value] : null;
    }
}
