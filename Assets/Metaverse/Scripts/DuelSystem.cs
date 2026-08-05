using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Duels in the arena. One scene object owns every match on the server: it pairs the two
/// players, counts them in, routes their damage, and decides who won. Players can only hurt
/// each other while a match of theirs is running, and nobody dies - the loser is left on one
/// hit point and both are healed at the bell.
/// </summary>
public class DuelSystem : NetworkBehaviour
{
    public static DuelSystem Instance { get; private set; }

    /// <summary>Set by the scene builder: only inside this circle can a duel start.</summary>
    public Vector3 ArenaCentre = new(0f, 0f, 120f);
    public float ArenaRadius = 22f;

    public Vector3 CornerA = new(0f, 1f, 106f);
    public Vector3 CornerB = new(0f, 1f, 134f);

    public float Countdown = 3f;
    public float RoundTime = 90f;
    public float InviteTimeout = 15f;

    /// <summary>Client 0 is the host, so a draw needs its own sentinel.</summary>
    const ulong NoWinner = ulong.MaxValue;

    class Match
    {
        public ulong A;
        public ulong B;
        public float StartTime;
        public bool Live;
    }

    readonly List<Match> matches = new();
    readonly Dictionary<ulong, ulong> invites = new();

    // Local UI state.
    string inviteFrom = "";
    ulong inviteFromId;
    float inviteExpiry;
    string opponentName = "";
    bool inDuel;
    float localEndTime;
    float localStartTime;

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
        if (IsServer)
        {
            TickMatches();
        }

        if (!IsClient || PlayerAvatar.Local == null || ChatSystem.IsTyping || ShopNpc.PanelOpen)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.gKey.wasPressedThisFrame && !inDuel)
        {
            ChallengeRpc();
        }
        else if (keyboard.hKey.wasPressedThisFrame && inviteFrom.Length > 0)
        {
            AcceptRpc(inviteFromId);
            inviteFrom = "";
        }
    }

    // ---------------------------------------------------------------- server

    /// <summary>Server side: the opponent this player may hit right now, or null.</summary>
    public PlayerStats OpponentOf(ulong clientId)
    {
        foreach (var match in matches)
        {
            if (!match.Live)
            {
                continue;
            }

            if (match.A == clientId)
            {
                return MetaverseUi.StatsOf(match.B);
            }

            if (match.B == clientId)
            {
                return MetaverseUi.StatsOf(match.A);
            }
        }

        return null;
    }

    /// <summary>Server side: called when a duel hit lands, to see whether that ends it.</summary>
    public void ReportDuelHit(PlayerStats loser)
    {
        if (!IsServer || loser == null || loser.Hp.Value > 1)
        {
            return;
        }

        Match match = MatchOf(loser.OwnerClientId);
        if (match != null)
        {
            Finish(match, loser.OwnerClientId == match.A ? match.B : match.A, "KO");
        }
    }

    void TickMatches()
    {
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Match match = matches[i];
            var a = MetaverseUi.PlayerObject(match.A);
            var b = MetaverseUi.PlayerObject(match.B);

            if (a == null || b == null)
            {
                Finish(match, a == null ? match.B : match.A, "상대 접속 종료");
                continue;
            }

            if (!match.Live && Time.time >= match.StartTime)
            {
                match.Live = true;
                AnnounceRpc(NetText.Trim512("시작!"), RpcTarget.Group(new[] { match.A, match.B }, RpcTargetUse.Temp));
                continue;
            }

            if (!match.Live)
            {
                continue;
            }

            // Walking out of the ring gives the match away.
            if (!InArena(a.transform.position))
            {
                Finish(match, match.B, "링 이탈");
                continue;
            }

            if (!InArena(b.transform.position))
            {
                Finish(match, match.A, "링 이탈");
                continue;
            }

            if (Time.time - match.StartTime > RoundTime)
            {
                Finish(match, NoWinner, "시간 초과");
            }
        }
    }

    [Rpc(SendTo.Server)]
    void ChallengeRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var sender = MetaverseUi.PlayerObject(senderId);
        if (sender == null || MatchOf(senderId) != null)
        {
            return;
        }

        if (!InArena(sender.transform.position))
        {
            NoticeRpc(NetText.Trim512("결투는 아레나 안에서만 가능합니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        NetworkObject best = null;
        float bestDistance = 12f;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.ClientId == senderId || client.PlayerObject == null || !InArena(client.PlayerObject.transform.position))
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
            NoticeRpc(NetText.Trim512("근처에 결투할 상대가 없습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        ulong targetId = best.OwnerClientId;
        if (MatchOf(targetId) != null)
        {
            NoticeRpc(NetText.Trim512("상대가 이미 결투 중입니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        invites[targetId] = senderId;
        InviteRpc(NetText.Trim64(MetaverseUi.NameOf(senderId)), senderId, RpcTarget.Single(targetId, RpcTargetUse.Temp));
        NoticeRpc(NetText.Trim512($"{MetaverseUi.NameOf(targetId)}에게 결투를 신청했습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
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
        if (MatchOf(senderId) != null || MatchOf(fromClientId) != null)
        {
            return;
        }

        var match = new Match { A = fromClientId, B = senderId, StartTime = Time.time + Countdown };
        matches.Add(match);

        Prepare(match.A, CornerA, CornerB);
        Prepare(match.B, CornerB, CornerA);

        StartRpc(NetText.Trim64(MetaverseUi.NameOf(match.B)), Countdown, RoundTime, RpcTarget.Single(match.A, RpcTargetUse.Temp));
        StartRpc(NetText.Trim64(MetaverseUi.NameOf(match.A)), Countdown, RoundTime, RpcTarget.Single(match.B, RpcTargetUse.Temp));
        ChatSystem.Announce($"{MetaverseUi.NameOf(match.A)} vs {MetaverseUi.NameOf(match.B)} 결투 시작!");
    }

    /// <summary>Server side: full health, on your own mark, facing the other side.</summary>
    void Prepare(ulong clientId, Vector3 corner, Vector3 facing)
    {
        var stats = MetaverseUi.StatsOf(clientId);
        if (stats != null)
        {
            stats.Heal();
        }

        PlaceRpc(corner, facing, RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    void Finish(Match match, ulong winner, string reason)
    {
        matches.Remove(match);

        MetaverseUi.StatsOf(match.A)?.Heal();
        MetaverseUi.StatsOf(match.B)?.Heal();

        var winnerStats = winner == NoWinner ? null : MetaverseUi.StatsOf(winner);
        if (winnerStats == null)
        {
            ChatSystem.Announce($"결투가 무승부로 끝났습니다 ({reason}).");
        }
        else
        {
            winnerStats.RecordDuel(true);
            MetaverseUi.StatsOf(winner == match.A ? match.B : match.A)?.RecordDuel(false);
            ChatSystem.Announce($"{MetaverseUi.NameOf(winner)}님이 결투에서 승리했습니다 ({reason}).");
        }

        EndRpc(RpcTarget.Group(new[] { match.A, match.B }, RpcTargetUse.Temp));
    }

    bool InArena(Vector3 position)
    {
        Vector3 flat = position - ArenaCentre;
        flat.y = 0f;
        return flat.magnitude <= ArenaRadius;
    }

    Match MatchOf(ulong clientId)
    {
        foreach (var match in matches)
        {
            if (match.A == clientId || match.B == clientId)
            {
                return match;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- client

    [Rpc(SendTo.SpecifiedInParams)]
    void InviteRpc(FixedString64Bytes from, ulong fromClientId, RpcParams rpcParams)
    {
        inviteFrom = from.ToString();
        inviteFromId = fromClientId;
        inviteExpiry = Time.time + InviteTimeout;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void StartRpc(FixedString64Bytes opponent, float countdown, float roundTime, RpcParams rpcParams)
    {
        inDuel = true;
        opponentName = opponent.ToString();
        localStartTime = Time.time + countdown;
        localEndTime = localStartTime + roundTime;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void PlaceRpc(Vector3 corner, Vector3 facing, RpcParams rpcParams)
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null)
        {
            return;
        }

        avatar.Teleport(corner);

        Vector3 look = facing - corner;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
        {
            avatar.transform.rotation = Quaternion.LookRotation(look);
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void AnnounceRpc(FixedString512Bytes text, RpcParams rpcParams)
    {
        ChatSystem.Local(text.ToString());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void NoticeRpc(FixedString512Bytes text, RpcParams rpcParams)
    {
        ChatSystem.Local(text.ToString());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void EndRpc(RpcParams rpcParams)
    {
        inDuel = false;
        opponentName = "";
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsClient || PlayerAvatar.Local == null)
        {
            return;
        }

        if (inDuel)
        {
            float remaining = Mathf.Max(0f, localEndTime - Time.time);
            string headline = Time.time < localStartTime
                ? $"준비...  {Mathf.CeilToInt(localStartTime - Time.time)}"
                : $"결투 vs {opponentName}   {Mathf.FloorToInt(remaining / 60f)}:{Mathf.FloorToInt(remaining % 60f):00}";

            GUI.Box(new Rect(Screen.width * 0.5f - 140f, 34f, 280f, 26f), headline);
            return;
        }

        if (inviteFrom.Length > 0 && Time.time < inviteExpiry)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 150f, 34f, 300f, 26f), $"{inviteFrom}님이 결투를 신청했습니다  -  [H] 수락");
            return;
        }

        // Standing in the ring, so say how to start one.
        Vector3 flat = PlayerAvatar.Local.transform.position - ArenaCentre;
        flat.y = 0f;
        if (flat.magnitude <= ArenaRadius)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 130f, 34f, 260f, 26f), "[G] 가장 가까운 상대에게 결투 신청");
        }
    }
}
