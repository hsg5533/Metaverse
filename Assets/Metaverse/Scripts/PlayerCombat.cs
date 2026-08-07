using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Melee attack for the local avatar. The client only asks; the server picks the target
/// in front of the attacker and applies the damage, so hits cannot be faked.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerCombat : NetworkBehaviour
{
    public float Range = 2.6f;
    public float ConeDegrees = 120f;
    public float Cooldown = 0.6f;

    PlayerStats stats;
    AvatarLimbAnimator limbAnimator;
    PlayerFishing fishing;
    float nextAttackTime;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        limbAnimator = GetComponent<AvatarLimbAnimator>();
        fishing = GetComponent<PlayerFishing>();
    }

    void Update()
    {
        if (!IsOwner || !IsSpawned || ChatSystem.IsTyping || ShopNpc.PanelOpen || MetaverseHUD.PointerOverHud)
        {
            return;
        }

        // Holding a rod over water: the button casts a line instead of swinging.
        if (fishing != null && fishing.Ready)
        {
            return;
        }

        var mouse = Mouse.current;
        bool pressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || MobileInput.ConsumeAttack();
        if (!pressed || Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + Cooldown;
        GetComponent<PlayerEmotes>()?.Stop();
        PlaySwing();
        AttackRpc();
    }

    [Rpc(SendTo.Server)]
    void AttackRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        // The owner already swung locally; everyone else is told to play the same motion.
        SwingRpc();

        // In a duel the swing goes at the other fighter; monsters are ignored entirely.
        var opponent = DuelSystem.Instance != null ? DuelSystem.Instance.OpponentOf(OwnerClientId) : null;
        if (opponent != null)
        {
            if (InReach(opponent.transform.position))
            {
                var avatar = GetComponent<PlayerAvatar>();
                opponent.TakeDuelDamage(stats.AttackPower, avatar != null ? avatar.Nickname.Value.ToString() : "Someone");
                DuelSystem.Instance.ReportDuelHit(opponent);
            }

            return;
        }

        var target = Monster.FindTarget(transform.position + Vector3.up * 0.8f, transform.forward, Range, ConeDegrees);
        if (target != null)
        {
            target.TakeDamage(stats.AttackPower, stats);
        }
    }

    /// <summary>Same cone the monster search uses, so duels and hunting feel identical.</summary>
    bool InReach(Vector3 position)
    {
        Vector3 to = position - transform.position;
        to.y = 0f;
        return to.magnitude <= Range && Vector3.Angle(transform.forward, to) <= ConeDegrees * 0.5f;
    }

    [Rpc(SendTo.NotOwner)]
    void SwingRpc()
    {
        PlaySwing();
    }

    void PlaySwing()
    {
        GameSound.Play(GameSound.Swing, transform.position);

        if (limbAnimator != null)
        {
            limbAnimator.PlayAttack();
        }
    }
}
