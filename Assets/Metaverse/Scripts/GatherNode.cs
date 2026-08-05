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

    public NetworkVariable<bool> Ready = new(true, writePerm: NetworkVariableWritePermission.Server);

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

        if (!Ready.Value || PlayerAvatar.Local == null || ChatSystem.IsTyping || ShopNpc.PanelOpen || !InRange())
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
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

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(senderId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        // The client asked, but the server decides whether they are actually standing here.
        if (Vector3.Distance(client.PlayerObject.transform.position, transform.position) > InteractRange + 1f)
        {
            return;
        }

        Ready.Value = false;
        readyTime = Time.time + Cooldown;

        var inventory = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.Add(Kind, Yield);
        }

        var stats = client.PlayerObject.GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.GainReward(Exp, 0);
        }

        var quests = client.PlayerObject.GetComponent<PlayerQuests>();
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

    bool InRange()
    {
        return Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) <= InteractRange;
    }

    void OnGUI()
    {
        if (!IsSpawned || !Ready.Value || PlayerAvatar.Local == null || !InRange())
        {
            return;
        }

        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * PromptHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var prompt = new GUIContent($"[E] Gather {Kind}");
        Vector2 size = GUI.skin.box.CalcSize(prompt);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y, size.x, size.y), prompt);
    }
}
