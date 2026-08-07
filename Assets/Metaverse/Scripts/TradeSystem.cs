using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player to player trading. One scene object owns every session on the server; clients only
/// send offers and confirmations and draw what the server tells them.
/// The swap happens in one step on the server, so nobody can hand over goods and get nothing.
/// </summary>
public class TradeSystem : NetworkBehaviour
{
    public float RequestRange = 6f;
    public float BreakRange = 12f;

    class Session
    {
        public ulong A;
        public ulong B;
        public int GoldA, OreA, HerbA, WoodA;
        public int GoldB, OreB, HerbB, WoodB;
        public bool ConfirmA, ConfirmB;
    }

    // Server state.
    readonly System.Collections.Generic.List<Session> sessions = new();
    readonly System.Collections.Generic.Dictionary<ulong, ulong> invites = new();

    // Local UI state, filled in by the server.
    bool open;
    string partnerName = "";
    int myGold, myOre, myHerb, myWood;
    int theirGold, theirOre, theirHerb, theirWood;
    bool myConfirm, theirConfirm;
    string inviteFrom = "";
    ulong inviteFromId;
    float inviteExpiry;

    void Update()
    {
        if (IsServer)
        {
            DropBrokenSessions();
        }

        if (!IsClient || PlayerAvatar.Local == null || ChatSystem.IsTyping)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if ((keyboard.tKey.wasPressedThisFrame || MobileInput.Pressed(Key.T)) && !open)
        {
            RequestNearestRpc();
        }
        else if ((keyboard.yKey.wasPressedThisFrame || MobileInput.Pressed(Key.Y)) && inviteFrom.Length > 0)
        {
            AcceptRpc(inviteFromId);
            inviteFrom = "";
        }
    }

    // ---------------------------------------------------------------- server

    [Rpc(SendTo.Server)]
    void RequestNearestRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var sender = MetaverseUi.PlayerObject(senderId);
        if (sender == null || SessionOf(senderId) != null)
        {
            return;
        }

        NetworkObject best = null;
        float bestDistance = RequestRange;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.ClientId == senderId || client.PlayerObject == null)
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
            NoticeRpc(NetText.Trim512("근처에 거래할 사람이 없습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        ulong targetId = best.OwnerClientId;
        if (SessionOf(targetId) != null)
        {
            NoticeRpc(NetText.Trim512("상대가 이미 거래 중입니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        invites[targetId] = senderId;
        InviteRpc(NetText.Trim64(MetaverseUi.NameOf(senderId)), senderId, RpcTarget.Single(targetId, RpcTargetUse.Temp));
        NoticeRpc(NetText.Trim512($"{MetaverseUi.NameOf(targetId)}에게 거래를 신청했습니다."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
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
        if (SessionOf(senderId) != null || SessionOf(fromClientId) != null)
        {
            return;
        }

        sessions.Add(new Session { A = fromClientId, B = senderId });
        PushState(sessions[^1]);
    }

    [Rpc(SendTo.Server)]
    void SetOfferRpc(int gold, int ore, int herb, int wood, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var session = SessionOf(senderId);
        if (session == null)
        {
            return;
        }

        // Any change means both sides have to look again.
        session.ConfirmA = false;
        session.ConfirmB = false;

        if (session.A == senderId)
        {
            session.GoldA = Mathf.Max(0, gold);
            session.OreA = Mathf.Max(0, ore);
            session.HerbA = Mathf.Max(0, herb);
            session.WoodA = Mathf.Max(0, wood);
        }
        else
        {
            session.GoldB = Mathf.Max(0, gold);
            session.OreB = Mathf.Max(0, ore);
            session.HerbB = Mathf.Max(0, herb);
            session.WoodB = Mathf.Max(0, wood);
        }

        PushState(session);
    }

    [Rpc(SendTo.Server)]
    void ConfirmRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var session = SessionOf(senderId);
        if (session == null)
        {
            return;
        }

        if (session.A == senderId)
        {
            session.ConfirmA = true;
        }
        else
        {
            session.ConfirmB = true;
        }

        if (session.ConfirmA && session.ConfirmB)
        {
            Settle(session);
            return;
        }

        PushState(session);
    }

    [Rpc(SendTo.Server)]
    void CancelRpc(RpcParams rpcParams = default)
    {
        var session = SessionOf(rpcParams.Receive.SenderClientId);
        if (session != null)
        {
            Close(session, "거래가 취소되었습니다.");
        }
    }

    void Settle(Session session)
    {
        var a = MetaverseUi.PlayerObject(session.A);
        var b = MetaverseUi.PlayerObject(session.B);
        if (a == null || b == null)
        {
            Close(session, "거래에 실패했습니다.");
            return;
        }

        var statsA = a.GetComponent<PlayerStats>();
        var statsB = b.GetComponent<PlayerStats>();
        var bagA = a.GetComponent<PlayerInventory>();
        var bagB = b.GetComponent<PlayerInventory>();

        bool canPay =
            statsA.Gold.Value >= session.GoldA && statsB.Gold.Value >= session.GoldB &&
            bagA.Ore.Value >= session.OreA && bagA.Herb.Value >= session.HerbA && bagA.Wood.Value >= session.WoodA &&
            bagB.Ore.Value >= session.OreB && bagB.Herb.Value >= session.HerbB && bagB.Wood.Value >= session.WoodB;

        if (!canPay)
        {
            Close(session, "거래 실패: 제시한 물건이 부족합니다.");
            return;
        }

        statsA.Gold.Value += session.GoldB - session.GoldA;
        statsB.Gold.Value += session.GoldA - session.GoldB;

        bagA.SetAll(
            bagA.Ore.Value - session.OreA + session.OreB,
            bagA.Herb.Value - session.HerbA + session.HerbB,
            bagA.Wood.Value - session.WoodA + session.WoodB);

        bagB.SetAll(
            bagB.Ore.Value - session.OreB + session.OreA,
            bagB.Herb.Value - session.HerbB + session.HerbA,
            bagB.Wood.Value - session.WoodB + session.WoodA);

        Close(session, "거래가 성사되었습니다.");
    }

    void DropBrokenSessions()
    {
        for (int i = sessions.Count - 1; i >= 0; i--)
        {
            var session = sessions[i];
            var a = MetaverseUi.PlayerObject(session.A);
            var b = MetaverseUi.PlayerObject(session.B);

            if (a == null || b == null)
            {
                Close(session, "상대가 나가서 거래가 종료되었습니다.");
            }
            else if (Vector3.Distance(a.transform.position, b.transform.position) > BreakRange)
            {
                Close(session, "너무 멀어져서 거래가 종료되었습니다.");
            }
        }
    }

    void Close(Session session, string reason)
    {
        sessions.Remove(session);
        var targets = RpcTarget.Group(new[] { session.A, session.B }, RpcTargetUse.Temp);
        ClosedRpc(NetText.Trim512(reason), targets);
    }

    void PushState(Session session)
    {
        StateRpc(NetText.Trim64(MetaverseUi.NameOf(session.B)),
            session.GoldA, session.OreA, session.HerbA, session.WoodA, session.ConfirmA,
            session.GoldB, session.OreB, session.HerbB, session.WoodB, session.ConfirmB,
            RpcTarget.Single(session.A, RpcTargetUse.Temp));

        StateRpc(NetText.Trim64(MetaverseUi.NameOf(session.A)),
            session.GoldB, session.OreB, session.HerbB, session.WoodB, session.ConfirmB,
            session.GoldA, session.OreA, session.HerbA, session.WoodA, session.ConfirmA,
            RpcTarget.Single(session.B, RpcTargetUse.Temp));
    }

    Session SessionOf(ulong clientId)
    {
        foreach (var session in sessions)
        {
            if (session.A == clientId || session.B == clientId)
            {
                return session;
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
        inviteExpiry = Time.time + 15f;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void StateRpc(FixedString64Bytes partner,
        int myGoldValue, int myOreValue, int myHerbValue, int myWoodValue, bool myConfirmValue,
        int theirGoldValue, int theirOreValue, int theirHerbValue, int theirWoodValue, bool theirConfirmValue,
        RpcParams rpcParams)
    {
        open = true;
        partnerName = partner.ToString();
        myGold = myGoldValue;
        myOre = myOreValue;
        myHerb = myHerbValue;
        myWood = myWoodValue;
        myConfirm = myConfirmValue;
        theirGold = theirGoldValue;
        theirOre = theirOreValue;
        theirHerb = theirHerbValue;
        theirWood = theirWoodValue;
        theirConfirm = theirConfirmValue;
        ShopNpc.PanelOpen = true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void ClosedRpc(FixedString512Bytes reason, RpcParams rpcParams)
    {
        open = false;
        ShopNpc.PanelOpen = false;
        ChatSystem.Local(reason.ToString());
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

        if (!open)
        {
            if (inviteFrom.Length > 0 && Time.time < inviteExpiry)
            {
                var box = new Rect(MetaverseUi.Width * 0.5f - 140f, 90f, 280f, 26f);
                GUI.Box(box, $"{inviteFrom}님이 거래를 신청했습니다  —  [Y] 수락");
            }
            return;
        }

        var area = new Rect(MetaverseUi.Width * 0.5f - 200f, MetaverseUi.Height * 0.5f - 130f, 400f, 260f);
        GUILayout.BeginArea(area, GUI.skin.box);

        GUILayout.Label($"<b>{partnerName}님과 거래 중</b>", MetaverseUi.Rich);
        GUILayout.Space(4);

        GUILayout.Label($"상대 제시:  골드 {theirGold}, 광석 {theirOre}, 약초 {theirHerb}, 나무 {theirWood}  {(theirConfirm ? "[확인함]" : "")}");
        GUILayout.Space(6);
        GUILayout.Label($"내 제시:  골드 {myGold}, 광석 {myOre}, 약초 {myHerb}, 나무 {myWood}  {(myConfirm ? "[확인함]" : "")}");

        DrawOfferRow("골드", ref myGold, 10);
        DrawOfferRow("광석", ref myOre, 1);
        DrawOfferRow("약초", ref myHerb, 1);
        DrawOfferRow("나무", ref myWood, 1);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(myConfirm ? "확인함" : "확인"))
        {
            ConfirmRpc();
        }
        if (GUILayout.Button("취소"))
        {
            CancelRpc();
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    void DrawOfferRow(string label, ref int value, int step)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(50));
        if (GUILayout.Button("-", GUILayout.Width(28)) && value > 0)
        {
            value = Mathf.Max(0, value - step);
            SendOffer();
        }
        GUILayout.Label(value.ToString(), GUILayout.Width(40));
        if (GUILayout.Button("+", GUILayout.Width(28)))
        {
            value += step;
            SendOffer();
        }
        GUILayout.EndHorizontal();
    }

    void SendOffer()
    {
        SetOfferRpc(myGold, myOre, myHerb, myWood);
    }
}
