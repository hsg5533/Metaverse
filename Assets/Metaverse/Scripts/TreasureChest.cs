using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The reward for clearing the boss chamber. A chest stays shut until the boss falls, then
/// the first player to reach it takes the haul and the lid closes again for the next round.
/// </summary>
public class TreasureChest : NetworkBehaviour
{
    public static readonly List<TreasureChest> All = new();

    /// <summary>Lid transform; it swings open while the chest is unlocked.</summary>
    public Transform Lid;

    public int Gold = 90;
    public int Ore = 2;
    public int Exp = 40;
    public float InteractRange = 3f;
    public float PromptHeight = 1.4f;

    public NetworkVariable<bool> Unlocked = new(false, writePerm: NetworkVariableWritePermission.Server);

    float lidAngle;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
    }

    /// <summary>Server side: called when a boss dies, so its chamber pays out.</summary>
    public static void UnlockAll()
    {
        foreach (var chest in All)
        {
            if (chest != null && chest.IsServer)
            {
                chest.Unlocked.Value = true;
            }
        }
    }

    void Update()
    {
        AnimateLid();

        if (PlayerAvatar.Local == null || ChatSystem.IsTyping || ShopNpc.PanelOpen || !InRange())
        {
            return;
        }

        if (Unlocked.Value && MetaverseUi.InteractPressed)
        {
            LootRpc();
        }
    }

    /// <summary>Local only: the lid follows the locked state instead of being replicated.</summary>
    void AnimateLid()
    {
        if (Lid == null)
        {
            return;
        }

        lidAngle = Mathf.Lerp(lidAngle, Unlocked.Value ? -78f : 0f, 6f * Time.deltaTime);
        Lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
    }

    [Rpc(SendTo.Server)]
    void LootRpc(RpcParams rpcParams = default)
    {
        if (!Unlocked.Value)
        {
            return;
        }

        var player = MetaverseUi.PlayerObject(rpcParams.Receive.SenderClientId);

        // The client asked, but the server decides whether they are actually standing here.
        if (player == null || Vector3.Distance(player.transform.position, transform.position) > InteractRange + 1f)
        {
            return;
        }

        Unlocked.Value = false;

        var stats = player.GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.Gold.Value += Gold;
            stats.GainReward(Exp, 0);
        }

        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.Add(GatherKind.Ore, Ore);
        }

        var avatar = player.GetComponent<PlayerAvatar>();
        string who = avatar != null ? avatar.Nickname.Value.ToString() : "누군가";
        ChatSystem.Announce($"{who}님이 보물상자를 열었습니다. (골드 {Gold}, 광석 {Ore})");
    }

    bool InRange()
    {
        return Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) <= InteractRange;
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsSpawned || PlayerAvatar.Local == null || !InRange())
        {
            return;
        }

        MetaverseUi.WorldPrompt(transform.position + Vector3.up * PromptHeight,
            Unlocked.Value ? "[E] 보물상자 열기" : "보스를 처치해야 열린다");
    }
}
