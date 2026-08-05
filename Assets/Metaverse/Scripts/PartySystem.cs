using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Parties of up to four. The server owns every group; clients only ask to invite, accept or
/// leave, and draw the roster. Kills are shared with the members standing nearby: experience
/// in full to each of them, gold split, and quest progress credited to everyone who was there.
/// </summary>
public class PartySystem : NetworkBehaviour
{
    public static PartySystem Instance { get; private set; }

    public const int MaxMembers = 4;

    public float InviteRange = 12f;
    public float ShareRange = 40f;
    public float InviteTimeout = 15f;

    readonly List<List<ulong>> parties = new();
    readonly Dictionary<ulong, ulong> invites = new();

    // Local roster, filled in by the server.
    ulong[] members = new ulong[0];
    string inviteFrom = "";
    ulong inviteFromId;
    float inviteExpiry;

    public override void OnNetworkSpawn()
    {
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (!IsClient || PlayerAvatar.Local == null || ChatSystem.IsTyping || ShopNpc.PanelOpen)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.oKey.wasPressedThisFrame)
        {
            InviteRpc();
        }
        else if (keyboard.uKey.wasPressedThisFrame && inviteFrom.Length > 0)
        {
            AcceptRpc(inviteFromId);
            inviteFrom = "";
        }
        else if (keyboard.lKey.wasPressedThisFrame && members.Length > 0)
        {
            LeaveRpc();
        }
    }

    // ---------------------------------------------------------------- server

    /// <summary>
    /// Server side: who a kill pays out to. Always at least the player who landed it, plus
    /// any party member close enough to have been part of the fight.
    /// </summary>
    public List<PlayerStats> Share(PlayerStats source)
    {
        var receivers = new List<PlayerStats> { source };
        if (source == null)
        {
            return receivers;
        }

        List<ulong> party = PartyOf(source.OwnerClientId);
        if (party == null)
        {
            return receivers;
        }

        foreach (ulong member in party)
        {
            if (member == source.OwnerClientId)
            {
                continue;
            }

            var stats = StatsOf(member);
            if (stats != null && Vector3.Distance(stats.transform.position, source.transform.position) <= ShareRange)
            {
                receivers.Add(stats);
            }
        }

        return receivers;
    }

    [Rpc(SendTo.Server)]
    void InviteRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var sender = PlayerObject(senderId);
        if (sender == null)
        {
            return;
        }

        List<ulong> party = PartyOf(senderId);
        if (party != null && party.Count >= MaxMembers)
        {
            NoticeRpc(NetText.Trim512($"파티는 최대 {MaxMembers}명까지입니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        NetworkObject best = null;
        float bestDistance = InviteRange;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.ClientId == senderId || client.PlayerObject == null || PartyOf(client.ClientId) != null)
            {
                continue;
            }

            float distance = Vector3.Distance(client.PlayerObject.transform.position, sender.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = client.PlayerObject;
            }
        }

        if (best == null)
        {
            NoticeRpc(NetText.Trim512("근처에 초대할 사람이 없습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        ulong targetId = best.OwnerClientId;
        invites[targetId] = senderId;
        InvitedRpc(NetText.Trim64(NameOf(senderId)), senderId, RpcTarget.Single(targetId, RpcTargetUse.Temp));
        NoticeRpc(NetText.Trim512($"{NameOf(targetId)}님을 파티에 초대했습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.Server)]
    void AcceptRpc(ulong fromClientId, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!invites.TryGetValue(senderId, out ulong expected) || expected != fromClientId)
        {
            return;
        }

        invites.Remove(senderId);
        if (PartyOf(senderId) != null)
        {
            return;
        }

        List<ulong> party = PartyOf(fromClientId);
        if (party == null)
        {
            party = new List<ulong> { fromClientId };
            parties.Add(party);
        }

        if (party.Count >= MaxMembers)
        {
            NoticeRpc(NetText.Trim512("그 파티는 이미 가득 찼습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        party.Add(senderId);
        Announce(party, $"{NameOf(senderId)}님이 파티에 참가했습니다.");
        PushRoster(party);
    }

    [Rpc(SendTo.Server)]
    void LeaveRpc(RpcParams rpcParams = default)
    {
        Remove(rpcParams.Receive.SenderClientId, "님이 파티에서 나갔습니다.");
    }

    /// <summary>Server side: also used when a client drops out of the session.</summary>
    void Remove(ulong clientId, string reason)
    {
        List<ulong> party = PartyOf(clientId);
        if (party == null)
        {
            return;
        }

        party.Remove(clientId);
        RosterRpc(new ulong[0], RpcTarget.Single(clientId, RpcTargetUse.Temp));
        Announce(party, $"{NameOf(clientId)}{reason}");

        // A party of one is not a party.
        if (party.Count <= 1)
        {
            foreach (ulong member in party)
            {
                RosterRpc(new ulong[0], RpcTarget.Single(member, RpcTargetUse.Temp));
            }

            parties.Remove(party);
            return;
        }

        PushRoster(party);
    }

    void LateUpdate()
    {
        if (!IsServer)
        {
            return;
        }

        // Drop anyone who left the session.
        for (int i = parties.Count - 1; i >= 0; i--)
        {
            for (int j = parties[i].Count - 1; j >= 0; j--)
            {
                if (PlayerObject(parties[i][j]) == null)
                {
                    Remove(parties[i][j], "님이 접속을 종료했습니다.");
                    break;
                }
            }
        }
    }

    void Announce(List<ulong> party, string text)
    {
        if (party.Count == 0)
        {
            return;
        }

        NoticeRpc(NetText.Trim512(text), RpcTarget.Group(party.ToArray(), RpcTargetUse.Temp));
    }

    void PushRoster(List<ulong> party)
    {
        ulong[] roster = party.ToArray();
        RosterRpc(roster, RpcTarget.Group(roster, RpcTargetUse.Temp));
    }

    List<ulong> PartyOf(ulong clientId)
    {
        foreach (var party in parties)
        {
            if (party.Contains(clientId))
            {
                return party;
            }
        }

        return null;
    }

    NetworkObject PlayerObject(ulong clientId)
    {
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ? client.PlayerObject : null;
    }

    PlayerStats StatsOf(ulong clientId)
    {
        var player = PlayerObject(clientId);
        return player != null ? player.GetComponent<PlayerStats>() : null;
    }

    string NameOf(ulong clientId)
    {
        var player = PlayerObject(clientId);
        var avatar = player != null ? player.GetComponent<PlayerAvatar>() : null;
        return avatar != null ? avatar.Nickname.Value.ToString() : "플레이어";
    }

    // ---------------------------------------------------------------- client

    [Rpc(SendTo.SpecifiedInParams)]
    void InvitedRpc(FixedString64Bytes from, ulong fromClientId, RpcParams rpcParams)
    {
        inviteFrom = from.ToString();
        inviteFromId = fromClientId;
        inviteExpiry = Time.time + InviteTimeout;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void RosterRpc(ulong[] roster, RpcParams rpcParams)
    {
        members = roster ?? new ulong[0];
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void NoticeRpc(FixedString512Bytes text, RpcParams rpcParams)
    {
        ChatSystem.Local(text.ToString());
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsClient || PlayerAvatar.Local == null)
        {
            return;
        }

        if (inviteFrom.Length > 0 && Time.time < inviteExpiry && members.Length == 0)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 150f, 66f, 300f, 26f), $"{inviteFrom}님의 파티 초대  -  [U] 수락");
        }

        if (members.Length == 0)
        {
            return;
        }

        float height = 30f + members.Length * 34f;
        GUILayout.BeginArea(new Rect(10, 250, 210, height), GUI.skin.box);
        GUILayout.Label($"<b>파티</b>  {members.Length}/{MaxMembers}   [L] 나가기", RichLabel());

        foreach (ulong member in members)
        {
            DrawMember(member);
        }

        GUILayout.EndArea();
    }

    void DrawMember(ulong clientId)
    {
        var manager = NetworkManager.Singleton;
        NetworkObject player = null;
        if (manager != null && manager.SpawnManager != null)
        {
            player = manager.SpawnManager.GetPlayerNetworkObject(clientId);
        }

        if (player == null)
        {
            GUILayout.Label("...");
            return;
        }

        var avatar = player.GetComponent<PlayerAvatar>();
        var stats = player.GetComponent<PlayerStats>();
        if (avatar == null || stats == null)
        {
            return;
        }

        GUILayout.Label($"{avatar.Nickname.Value}  Lv.{stats.Level.Value}");

        // A slim health bar, read straight off the replicated values.
        Rect bar = GUILayoutUtility.GetRect(190f, 8f);
        float fill = stats.MaxHp > 0 ? Mathf.Clamp01(stats.Hp.Value / (float)stats.MaxHp) : 0f;

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.35f, 0.8f, 0.4f, 0.95f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fill, bar.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
