using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// RPG progression for one avatar: level, experience, gold, health and weapon.
/// Every value is written by the server only; clients read them to draw the HUD.
/// </summary>
[RequireComponent(typeof(PlayerAvatar))]
public class PlayerStats : NetworkBehaviour
{
    /// <summary>Half width of the walled village; outside it health does not come back on its own.</summary>
    public float VillageHalfSize = 32f;
    public float RegenInterval = 1f;
    public int RegenPercentPerTick = 4;

    public NetworkVariable<int> Level = new(1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Exp = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Gold = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Hp = new(100, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> WeaponLevel = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> ArmorLevel = new(0, writePerm: NetworkVariableWritePermission.Server);

    public int MaxHp => 100 + (Level.Value - 1) * 20;
    public int AttackPower => 8 + (Level.Value - 1) * 3 + WeaponLevel.Value * 4;
    public int Defense => 8 + (Level.Value - 1) * 2 + ArmorLevel.Value * 3;
    public int ExpToNextLevel => 40 + (Level.Value - 1) * 30;
    public int WeaponPrice => 60 * (WeaponLevel.Value + 1);
    public int ArmorPrice => 50 * (ArmorLevel.Value + 1);

    PlayerAvatar avatar;
    AvatarLimbAnimator limbAnimator;
    float nextRegenTime;

    void Awake()
    {
        avatar = GetComponent<PlayerAvatar>();
        limbAnimator = GetComponent<AvatarLimbAnimator>();
    }

    public override void OnNetworkSpawn()
    {
        // Health is readable by everyone, so every peer can play the flinch without an extra RPC.
        Hp.OnValueChanged += OnHpChanged;

        if (IsServer)
        {
            Hp.Value = MaxHp;
        }
    }

    public override void OnNetworkDespawn()
    {
        Hp.OnValueChanged -= OnHpChanged;
    }

    /// <summary>Server side: resting in the village ticks health back up.</summary>
    void Update()
    {
        if (!IsServer || !IsSpawned || Time.time < nextRegenTime)
        {
            return;
        }

        nextRegenTime = Time.time + RegenInterval;

        if (Hp.Value >= MaxHp || !InVillage)
        {
            return;
        }

        Hp.Value = Mathf.Min(MaxHp, Hp.Value + Mathf.Max(1, MaxHp * RegenPercentPerTick / 100));
    }

    bool InVillage =>
        Mathf.Abs(transform.position.x) <= VillageHalfSize &&
        Mathf.Abs(transform.position.z) <= VillageHalfSize;

    void OnHpChanged(int previous, int current)
    {
        if (current < previous && limbAnimator != null)
        {
            limbAnimator.PlayHit();
        }
    }

    /// <summary>Server side: pay out a kill and level up as many times as the experience allows.</summary>
    public void GainReward(int exp, int gold)
    {
        if (!IsServer)
        {
            return;
        }

        Exp.Value += exp;
        Gold.Value += gold;
        NoticeRpc(NetText.Trim64($"+{exp} EXP  +{gold} G"));

        while (Exp.Value >= ExpToNextLevel)
        {
            Exp.Value -= ExpToNextLevel;
            Level.Value++;
            Hp.Value = MaxHp;
            NoticeRpc(NetText.Trim64($"LEVEL UP!  Lv.{Level.Value}  (HP {MaxHp}, ATK {AttackPower}, DEF {Defense})"));
        }
    }

    /// <summary>
    /// Server side: apply monster damage, respawning the avatar in the village at zero health.
    /// Armour soaks up part of the hit but never all of it.
    /// </summary>
    public void TakeDamage(int amount, string source)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        Hp.Value = Mathf.Max(0, Hp.Value - Mathf.Max(1, amount - Defense));
        if (Hp.Value > 0)
        {
            return;
        }

        Hp.Value = MaxHp;
        NoticeRpc(NetText.Trim64($"You were knocked out by {source}. Back to the village."));
        RespawnRpc();
    }

    [Rpc(SendTo.Server)]
    public void BuyArmorRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        int price = ArmorPrice;
        if (Gold.Value < price)
        {
            NoticeRpc(NetText.Trim64("Not enough gold."));
            return;
        }

        Gold.Value -= price;
        ArmorLevel.Value++;
        NoticeRpc(NetText.Trim64($"Armor Lv.{ArmorLevel.Value}!  DEF {Defense}"));
    }

    [Rpc(SendTo.Server)]
    public void BuyWeaponRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        int price = WeaponPrice;
        if (Gold.Value < price)
        {
            NoticeRpc(NetText.Trim64("Not enough gold."));
            return;
        }

        Gold.Value -= price;
        WeaponLevel.Value++;
        NoticeRpc(NetText.Trim64($"Weapon Lv.{WeaponLevel.Value}!  ATK {AttackPower}"));
    }

    [Rpc(SendTo.Owner)]
    void RespawnRpc()
    {
        avatar.Teleport(PlayerAvatar.SpawnPointFor(OwnerClientId));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString64Bytes text)
    {
        ChatSystem.Local(text.ToString());
    }

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 146), GUI.skin.box);
        GUILayout.Label($"<b>Lv.{Level.Value}</b>  {Nickname()}", RichLabel());
        GUILayout.Label($"HP    {Hp.Value} / {MaxHp}{(InVillage && Hp.Value < MaxHp ? "  (resting)" : "")}");
        GUILayout.Label($"EXP   {Exp.Value} / {ExpToNextLevel}");
        GUILayout.Label($"Gold  {Gold.Value}");
        GUILayout.Label($"ATK   {AttackPower}  (Weapon Lv.{WeaponLevel.Value})");
        GUILayout.Label($"DEF   {Defense}  (Armor Lv.{ArmorLevel.Value})");
        GUILayout.EndArea();
    }

    string Nickname()
    {
        return avatar != null ? avatar.Nickname.Value.ToString() : "";
    }

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
