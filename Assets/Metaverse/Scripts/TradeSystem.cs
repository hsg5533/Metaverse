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

        if (keyboard.tKey.wasPressedThisFrame && !open)
        {
            RequestNearestRpc();
        }
        else if (keyboard.yKey.wasPressedThisFrame && inviteFrom.Length > 0)
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
        var sender = PlayerObject(senderId);
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
            NoticeRpc(NetText.Trim64("Nobody close enough to trade with."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        ulong targetId = best.OwnerClientId;
        if (SessionOf(targetId) != null)
        {
            NoticeRpc(NetText.Trim64("They are already trading."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
            return;
        }

        invites[targetId] = senderId;
        InviteRpc(NetText.Trim64(NameOf(senderId)), senderId, RpcTarget.Single(targetId, RpcTargetUse.Temp));
        NoticeRpc(NetText.Trim64($"Trade offered to {NameOf(targetId)}."), RpcTarget.Single(senderId, RpcTargetUse.Temp));
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
            Close(session, "Trade cancelled.");
        }
    }

    void Settle(Session session)
    {
        var a = PlayerObject(session.A);
        var b = PlayerObject(session.B);
        if (a == null || b == null)
        {
            Close(session, "Trade failed.");
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
            Close(session, "Trade failed: someone no longer has what they offered.");
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

        Close(session, "Trade complete.");
    }

    void DropBrokenSessions()
    {
        for (int i = sessions.Count - 1; i >= 0; i--)
        {
            var session = sessions[i];
            var a = PlayerObject(session.A);
            var b = PlayerObject(session.B);

            if (a == null || b == null)
            {
                Close(session, "Trade closed: the other player left.");
            }
            else if (Vector3.Distance(a.transform.position, b.transform.position) > BreakRange)
            {
                Close(session, "Trade closed: too far apart.");
            }
        }
    }

    void Close(Session session, string reason)
    {
        sessions.Remove(session);
        var targets = RpcTarget.Group(new[] { session.A, session.B }, RpcTargetUse.Temp);
        ClosedRpc(NetText.Trim64(reason), targets);
    }

    void PushState(Session session)
    {
        StateRpc(NetText.Trim64(NameOf(session.B)),
            session.GoldA, session.OreA, session.HerbA, session.WoodA, session.ConfirmA,
            session.GoldB, session.OreB, session.HerbB, session.WoodB, session.ConfirmB,
            RpcTarget.Single(session.A, RpcTargetUse.Temp));

        StateRpc(NetText.Trim64(NameOf(session.A)),
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

    NetworkObject PlayerObject(ulong clientId)
    {
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ? client.PlayerObject : null;
    }

    string NameOf(ulong clientId)
    {
        var player = PlayerObject(clientId);
        var avatar = player != null ? player.GetComponent<PlayerAvatar>() : null;
        return avatar != null ? avatar.Nickname.Value.ToString() : "Player";
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
    void ClosedRpc(FixedString64Bytes reason, RpcParams rpcParams)
    {
        open = false;
        ShopNpc.PanelOpen = false;
        ChatSystem.Local(reason.ToString());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void NoticeRpc(FixedString64Bytes text, RpcParams rpcParams)
    {
        ChatSystem.Local(text.ToString());
    }

    void OnGUI()
    {
        if (!IsClient || PlayerAvatar.Local == null)
        {
            return;
        }

        if (!open)
        {
            if (inviteFrom.Length > 0 && Time.time < inviteExpiry)
            {
                var box = new Rect(Screen.width * 0.5f - 140f, 90f, 280f, 26f);
                GUI.Box(box, $"{inviteFrom} wants to trade  —  [Y] accept");
            }
            return;
        }

        var area = new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 130f, 400f, 260f);
        GUILayout.BeginArea(area, GUI.skin.box);

        GUILayout.Label($"<b>Trading with {partnerName}</b>", RichLabel());
        GUILayout.Space(4);

        GUILayout.Label($"They offer:  {theirGold} G, {theirOre} ore, {theirHerb} herb, {theirWood} wood  {(theirConfirm ? "[confirmed]" : "")}");
        GUILayout.Space(6);
        GUILayout.Label($"You offer:  {myGold} G, {myOre} ore, {myHerb} herb, {myWood} wood  {(myConfirm ? "[confirmed]" : "")}");

        DrawOfferRow("Gold", ref myGold, 10);
        DrawOfferRow("Ore", ref myOre, 1);
        DrawOfferRow("Herb", ref myHerb, 1);
        DrawOfferRow("Wood", ref myWood, 1);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(myConfirm ? "Confirmed" : "Confirm"))
        {
            ConfirmRpc();
        }
        if (GUILayout.Button("Cancel"))
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

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
