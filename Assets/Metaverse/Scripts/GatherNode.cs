using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A rock, bush or tree standing in the world. Press E next to it to harvest; the server
/// checks the distance, hands out the material and puts the node on a cooldown.
/// Same shape as a monster respawning, without the chasing.
/// </summary>
public class GatherNode : NetworkBehaviour
{
    public GatherKind Kind = GatherKind.Ore;
    public int Yield = 1;
    public int Exp = 3;
    public float Cooldown = 8f;
    public float InteractRange = 3f;
    public float PromptHeight = 1.8f;

    public NetworkVariable<bool> Ready = new(
        true,
        writePerm: NetworkVariableWritePermission.Server
    );

    Renderer[] visuals;
    Collider[] blockers;
    float readyTime;
    bool visualsReady = true;

    void Awake()
    {
        visuals = GetComponentsInChildren<Renderer>();
        blockers = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        SyncVisuals();

        if (IsServer && !Ready.Value && Time.time >= readyTime)
        {
            Ready.Value = true;
        }

        if (
            !Ready.Value
            || PlayerAvatar.Local == null
            || ChatSystem.IsTyping
            || ShopNpc.PanelOpen
            || !InRange()
        )
        {
            return;
        }

        if (MetaverseUi.InteractPressed)
        {
            GatherRpc();
        }
    }

    [Rpc(SendTo.Server)]
    void GatherRpc(RpcParams rpcParams = default)
    {
        if (!Ready.Value)
        {
            return;
        }

        var player = MetaverseUi.PlayerObject(rpcParams.Receive.SenderClientId);

        // The client asked, but the server decides whether they are actually standing here.
        if (
            player == null
            || Vector3.Distance(player.transform.position, transform.position) > InteractRange + 1f
        )
        {
            return;
        }

        Ready.Value = false;
        readyTime = Time.time + Cooldown;

        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.Add(Kind, Yield);
        }

        var stats = player.GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.GainReward(Exp, 0);
        }

        var quests = player.GetComponent<PlayerQuests>();
        if (quests != null)
        {
            quests.OnGathered(Kind, Yield);
        }
    }

    void SyncVisuals()
    {
        if (Ready.Value == visualsReady)
        {
            return;
        }

        visualsReady = Ready.Value;
        foreach (var renderer in visuals)
        {
            renderer.enabled = visualsReady;
        }
        foreach (var blocker in blockers)
        {
            blocker.enabled = visualsReady;
        }
    }

    string KoreanName =>
        Kind switch
        {
            GatherKind.Ore => "광석",
            GatherKind.Herb => "약초",
            _ => "나무",
        };

    bool InRange()
    {
        return Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position)
            <= InteractRange;
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsSpawned || !Ready.Value || PlayerAvatar.Local == null || !InRange())
        {
            return;
        }

        MetaverseUi.WorldPrompt(
            transform.position + Vector3.up * PromptHeight,
            $"[E] {KoreanName} 채집"
        );
    }
}
