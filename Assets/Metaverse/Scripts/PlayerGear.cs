using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gear the player carries, whether it dropped off a monster or was bought at the shop: what
/// is carried, what is worn, and which model shows on the body. The server owns all three, so
/// every client sees the same sword in the same hand.
/// </summary>
public class PlayerGear : NetworkBehaviour
{
    public readonly struct Piece
    {
        public readonly string Name;
        public readonly bool Weapon;
        public readonly int Bonus;

        /// <summary>The ground it drops on, which is also the model to show. -1 for food, which builds its own bowl instead.</summary>
        public readonly int Theme;

        /// <summary>False for a fish or a dish: it sits in the bag and is sold or eaten, never worn.</summary>
        public readonly bool Wearable;

        /// <summary>The buff eating this applies, or -1 for anything that is not food.</summary>
        public readonly int Buff;

        /// <summary>How long that buff lasts.</summary>
        public readonly float BuffSeconds;

        public bool IsFood => Buff >= 0;

        public Piece(string name, bool weapon, int bonus, int theme, bool wearable = true, int buff = -1, float buffSeconds = 0f)
        {
            Name = name;
            Weapon = weapon;
            Bonus = bonus;
            Theme = theme;
            Wearable = wearable;
            Buff = buff;
            BuffSeconds = buffSeconds;
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

        // Past here nothing is dropped by a monster: PieceFor only reaches the themed pairs.
        new("낚싯대", true, 0, 3),

        // The catch. Not worn, so its "armour model" slot is only ever used by the icon.
        new("붕어", false, 3, 3, false),
        new("잉어", false, 6, 4, false),
        new("메기", false, 9, 5, false),
        new("무지개송어", false, 12, 6, false),
        new("황금잉어", false, 33, 7, false),

        // The shop's own stock: never dropped by anything, only ever bought outright. A tier
        // below the ground's steel and a tier above its lava, so there is a reason to buy at
        // every point in the run instead of only farming.
        new("가죽 검", true, 4, 4),
        new("가죽 갑옷", false, 3, 8),
        new("은 검", true, 12, 5),
        new("은 갑옷", false, 9, 9),
        new("미스릴 검", true, 24, 6),
        new("미스릴 갑옷", false, 18, 10),

        // The campfire's dishes: cooking no longer applies the buff on the spot, it hands over
        // one of these instead, and eating it - a click in the bag - is what applies it.
        new("약초 스튜", false, 0, -1, false, PlayerBuffs.Attack, 180f),
        new("철분 수프", false, 0, -1, false, PlayerBuffs.Defense, 180f),
        new("여행자의 차", false, 0, -1, false, PlayerBuffs.Speed, 120f),
        new("생선구이", false, 0, -1, false, PlayerBuffs.Attack, 180f),
    };

    /// <summary>The rod, which the shop sells and the lake needs.</summary>
    public const int Rod = 6;

    /// <summary>The first fish; the rest follow it in order.</summary>
    public const int FirstFish = 7;

    /// <summary>Where the shop's own gear starts in <see cref="Pieces"/>, past the ground's drops and the catch.</summary>
    public const int ShopFirst = 12;

    /// <summary>Three tiers, a weapon and an armour each.</summary>
    public const int ShopCount = 6;

    /// <summary>Where the campfire's dishes start in <see cref="Pieces"/>, past the shop's own gear.</summary>
    public const int FoodFirst = 18;

    /// <summary>
    /// A material stack keeps its count in PlayerInventory, but takes a place in this list so
    /// everything the player owns sits in one order: the order it was picked up in.
    /// Negative, so it can never be mistaken for a piece.
    /// </summary>
    public static int MarkOf(int material) => -1 - material;

    public static int MaterialOf(int entry) => -1 - entry;

    /// <summary>Server side: the stack just went from nothing to something, or back.</summary>
    public void Mark(int material, bool carried)
    {
        if (!IsServer)
        {
            return;
        }

        int mark = MarkOf(material);
        int at = Bag.IndexOf(mark);

        if (carried && at < 0)
        {
            Bag.Add(mark);
        }
        else if (!carried && at >= 0)
        {
            Bag.RemoveAt(at);
        }
    }

    public bool HoldingRod => Weapon.Value == Rod;

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

    /// <summary>
    /// What the shop charges for one of its own pieces, bought outright instead of found on a
    /// monster. Priced above <see cref="PriceOf"/> so buying one and selling it back is never a
    /// way to make gold.
    /// </summary>
    public static int BuyPriceOf(int piece) => Pieces[piece].Bonus * 20;

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

    /// <summary>The piece index a monster of this ground drops: the themed pairs only.</summary>
    public static int PieceFor(int theme, bool weapon)
    {
        return Mathf.Clamp(theme, 0, 2) * 2 + (weapon ? 0 : 1);
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

    /// <summary>Server side: a recipe checking whether it has the fish it needs.</summary>
    public bool HasInBag(int piece) => Bag.IndexOf(piece) >= 0;

    /// <summary>Server side: a recipe spending the fish it needed. False if it was already gone.</summary>
    public bool TakeFromBag(int piece)
    {
        int at = IsServer ? Bag.IndexOf(piece) : -1;
        if (at < 0)
        {
            return false;
        }

        Bag.RemoveAt(at);
        return true;
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
        if (piece < 0 || !Pieces[piece].Wearable)
        {
            NoticeRpc(NetText.Trim512($"{Pieces[piece].Name}은(는) 입을 수 없습니다. 상인에게 파세요."));
            return;
        }

        var slot = Pieces[piece].Weapon ? Weapon : Armor;
        int previous = slot.Value;

        slot.Value = piece;
        Bag.RemoveAt(bagIndex);

        if (previous >= 0)
        {
            Bag.Add(previous);
        }
    }

    /// <summary>Server side: the shopkeeper hands one over for gold.</summary>
    [Rpc(SendTo.Server)]
    public void BuyRodRpc(RpcParams rpcParams = default)
    {
        var stats = GetComponent<PlayerStats>();
        if (rpcParams.Receive.SenderClientId != OwnerClientId || stats == null)
        {
            return;
        }

        if (stats.Gold.Value < RodPrice)
        {
            NoticeRpc(NetText.Trim512("골드가 부족합니다."));
            return;
        }

        stats.Gold.Value -= RodPrice;
        Give(Rod);
    }

    public const int RodPrice = 120;

    /// <summary>Server side: the shopkeeper hands over one of its own weapons or armour for gold.</summary>
    [Rpc(SendTo.Server)]
    public void BuyPieceRpc(int piece, RpcParams rpcParams = default)
    {
        var stats = GetComponent<PlayerStats>();
        if (rpcParams.Receive.SenderClientId != OwnerClientId || stats == null || piece < ShopFirst || piece >= ShopFirst + ShopCount)
        {
            return;
        }

        int price = BuyPriceOf(piece);
        if (stats.Gold.Value < price)
        {
            NoticeRpc(NetText.Trim512("골드가 부족합니다."));
            return;
        }

        stats.Gold.Value -= price;
        Give(piece);
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
        if (piece < 0)
        {
            return;
        }

        Bag.RemoveAt(bagIndex);

        var stats = GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.Gold.Value += PriceOf(piece);
        }

        NoticeRpc(NetText.Trim512($"{Pieces[piece].Name}을(를) {PriceOf(piece)} 골드에 팔았습니다."));
    }

    /// <summary>Eat or drink a dish from the bag: applies its buff and removes it.</summary>
    [Rpc(SendTo.Server)]
    public void UseFoodRpc(int bagIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || bagIndex < 0 || bagIndex >= Bag.Count)
        {
            return;
        }

        int piece = Bag[bagIndex];
        if (piece < 0 || !Pieces[piece].IsFood)
        {
            return;
        }

        Bag.RemoveAt(bagIndex);

        var buffs = GetComponent<PlayerBuffs>();
        if (buffs != null)
        {
            buffs.Apply(Pieces[piece].Buff, Pieces[piece].BuffSeconds);
        }

        NoticeRpc(NetText.Trim512(
            $"{Pieces[piece].Name}을(를) 먹었습니다. {PlayerBuffs.NameOf(Pieces[piece].Buff)} {Mathf.RoundToInt(Pieces[piece].BuffSeconds / 60f)}분."));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Local(text.ToString());
    }

    /// <summary>
    /// Every object a piece is made of: a sword is one, a suit of armour is three - the chest
    /// and the two shoulders that hang off the arms. The icons clone exactly this, so what is
    /// in the window is what is on the body.
    /// </summary>
    public GameObject[] PartsFor(int piece)
    {
        if (!Valid(piece) || Pieces[piece].IsFood)
        {
            // Food has no avatar model of its own; GearPreview builds it a bowl instead.
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
