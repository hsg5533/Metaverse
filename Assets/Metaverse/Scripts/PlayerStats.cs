using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// RPG progression for one avatar: level, experience, gold, health and weapon.
/// Every value is written by the server only; clients read them to draw the HUD.
/// </summary>
[RequireComponent(typeof(PlayerAvatar))]
public class PlayerStats : NetworkBehaviour
{
    /// <summary>True while the local player has the character sheet open.</summary>
    public static bool WindowOpen;

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
    public NetworkVariable<int> DuelWins = new(0, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> DuelLosses = new(0, writePerm: NetworkVariableWritePermission.Server);


    public int MaxHp => 100 + (Level.Value - 1) * 20;
    public int AttackPower => 8 + (Level.Value - 1) * 3 + WeaponLevel.Value * 4 + (buffs != null ? buffs.AttackBonus : 0);
    public int Defense => 8 + (Level.Value - 1) * 2 + ArmorLevel.Value * 3 + (buffs != null ? buffs.DefenseBonus : 0);
    public int ExpToNextLevel => 40 + (Level.Value - 1) * 30;
    public int WeaponPrice => 60 * (WeaponLevel.Value + 1);
    public int ArmorPrice => 50 * (ArmorLevel.Value + 1);

    /// <summary>Gear is named by the level it has been upgraded to, the same number the shop quotes.</summary>
    public string WeaponName => $"검 Lv.{WeaponLevel.Value}";
    public string ArmorName => $"방어구 Lv.{ArmorLevel.Value}";

    /// <summary>What the gear adds on its own, without the level or a buff behind it.</summary>
    public int WeaponBonus => WeaponLevel.Value * 4;
    public int ArmorBonus => ArmorLevel.Value * 3;

    PlayerAvatar avatar;
    AvatarLimbAnimator limbAnimator;
    PlayerBuffs buffs;
    float nextRegenTime;

    void Awake()
    {
        avatar = GetComponent<PlayerAvatar>();
        limbAnimator = GetComponent<AvatarLimbAnimator>();
        buffs = GetComponent<PlayerBuffs>();
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

        if (IsOwner)
        {
            WindowOpen = false;
        }
    }

    void Update()
    {
        HandleWindowKey();
        Regenerate();
    }

    /// <summary>The sheet is off by default; P brings it up.</summary>
    void HandleWindowKey()
    {
        if (!IsOwner || !IsSpawned || ChatSystem.IsTyping)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
        {
            WindowOpen = !WindowOpen;
        }
    }

    /// <summary>Server side: resting in the village ticks health back up.</summary>
    void Regenerate()
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
        NoticeRpc(NetText.Trim512($"경험치 +{exp}, 골드 +{gold}"));

        while (Exp.Value >= ExpToNextLevel)
        {
            Exp.Value -= ExpToNextLevel;
            Level.Value++;
            Hp.Value = MaxHp;
            NoticeRpc(NetText.Trim512($"레벨 업! Lv.{Level.Value} (체력 {MaxHp}, 공격 {AttackPower}, 방어 {Defense})"));
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
        NoticeRpc(NetText.Trim512($"{source}에게 쓰러졌습니다. 마을로 돌아갑니다."));
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
            NoticeRpc(NetText.Trim512("골드가 부족합니다."));
            return;
        }

        Gold.Value -= price;
        ArmorLevel.Value++;
        NoticeRpc(NetText.Trim512($"방어구 Lv.{ArmorLevel.Value}! 방어력 {Defense}"));
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
            NoticeRpc(NetText.Trim512("골드가 부족합니다."));
            return;
        }

        Gold.Value -= price;
        WeaponLevel.Value++;
        NoticeRpc(NetText.Trim512($"검 Lv.{WeaponLevel.Value}! 공격력 {AttackPower}"));
    }

    /// <summary>
    /// Server side: a hit from another player in the arena. Duels never kill - the loser is
    /// left standing on one hit point and the match ends there.
    /// </summary>
    public void TakeDuelDamage(int amount, string source)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        Hp.Value = Mathf.Max(1, Hp.Value - Mathf.Max(1, amount - Defense));
        if (Hp.Value <= 1)
        {
            NoticeRpc(NetText.Trim512($"{source}에게 패배했습니다."));
        }
    }

    /// <summary>Server side: back to full, used at both ends of a duel.</summary>
    public void Heal()
    {
        if (IsServer)
        {
            Hp.Value = MaxHp;
        }
    }

    /// <summary>Server side: one more line in the arena record.</summary>
    public void RecordDuel(bool won)
    {
        if (!IsServer)
        {
            return;
        }

        if (won)
        {
            DuelWins.Value++;
        }
        else
        {
            DuelLosses.Value++;
        }
    }

    /// <summary>Server side: used by the save file.</summary>
    public void RestoreDuels(int wins, int losses)
    {
        if (!IsServer)
        {
            return;
        }

        DuelWins.Value = Mathf.Max(0, wins);
        DuelLosses.Value = Mathf.Max(0, losses);
    }

    [Rpc(SendTo.Owner)]
    void RespawnRpc()
    {
        avatar.Teleport(PlayerAvatar.SpawnPointFor(OwnerClientId));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Local(text.ToString());
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsOwner || !IsSpawned || !WindowOpen)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(Screen.width - 250, 10, 240, 220), GUI.skin.box);
        GUILayout.Label($"<b>Lv.{Level.Value}</b>  {Nickname()}   [P] 닫기", MetaverseUi.Rich);
        GUILayout.Label($"체력    {Hp.Value} / {MaxHp}{(InVillage && Hp.Value < MaxHp ? "  (휴식 중)" : "")}");
        GUILayout.Label($"경험치  {Exp.Value} / {ExpToNextLevel}");
        GUILayout.Label($"골드    {Gold.Value}");
        GUILayout.Label($"공격력  {AttackPower}  ({WeaponName})");
        GUILayout.Label($"방어력  {Defense}  ({ArmorName})");
        if (DuelWins.Value + DuelLosses.Value > 0)
        {
            GUILayout.Label($"결투 {DuelWins.Value}승 {DuelLosses.Value}패");
        }
        GUILayout.EndArea();
    }

    string Nickname()
    {
        return avatar != null ? avatar.Nickname.Value.ToString() : "";
    }
}
