using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Scene placed chat relay. Clients send to the server, the server stamps the sender
/// nickname and broadcasts to everyone.
/// </summary>
public class ChatSystem : NetworkBehaviour
{
    /// <summary>True while the chat input has keyboard focus, so movement input is ignored.</summary>
    public static bool IsTyping;

    static ChatSystem instance;

    /// <summary>Server side: tells everyone in the world about something, e.g. a boss dying.</summary>
    public static void Announce(string text)
    {
        if (instance != null && instance.IsServer)
        {
            instance.BroadcastChatRpc(new Unity.Collections.FixedString64Bytes("월드"), NetText.Trim512(text));
        }
    }

    /// <summary>Writes a line only this client sees, used for combat and shop feedback.</summary>
    public static void Local(string line)
    {
        if (instance != null)
        {
            instance.AddLine(line);
        }
    }

    const string ControlName = "metaverseChatInput";
    const int MaxLines = 12;

    readonly List<string> lines = new();
    string draft = "";
    Vector2 scroll;

    public override void OnNetworkSpawn()
    {
        instance = this;
        lines.Clear();
        AddLine("<i>Enter를 눌러 채팅합니다.</i>");
    }

    public override void OnNetworkDespawn()
    {
        IsTyping = false;
        if (instance == this)
        {
            instance = null;
        }
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsSpawned)
        {
            return;
        }

        bool focused = GUI.GetNameOfFocusedControl() == ControlName;
        bool enterPressed = Event.current.type == EventType.KeyDown &&
                            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        if (enterPressed && !focused)
        {
            GUI.FocusControl(ControlName);
            Event.current.Use();
        }

        // A window on top would be clicked through: the field underneath still takes the
        // touch and the soft keyboard comes up over a shop nobody asked to leave.
        bool covered = ShopNpc.PanelOpen || PlayerInventory.WindowOpen || MetaverseHUD.MenuOpen;
        if (covered)
        {
            IsTyping = false;
            if (focused)
            {
                GUI.FocusControl(null);
            }

            return;
        }

        // On a touch screen the log shrinks to three lines and sits centred just above the
        // health bar: the bottom left is the stick and the bottom right is the buttons.
        // While typing it moves to the top, out from under the soft keyboard.
        bool touch = MobileInput.Active;
        float height = touch ? 112f : 200f;
        var area = touch
            ? new Rect(MetaverseUi.Width * 0.5f - 190f, IsTyping ? 16f : MetaverseUi.Height - 54f - height, 380f, height)
            : new Rect(10f, MetaverseUi.Height - 210f, 380f, height);
        GUILayout.BeginArea(area, GUI.skin.box);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(touch ? 62f : 150f));
        foreach (string line in lines)
        {
            GUILayout.Label(line, ChatLabel());
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUI.SetNextControlName(ControlName);
        draft = GUILayout.TextField(draft, 120);

        // No Enter key on a phone, so the field needs a button beside it.
        bool sent = touch && GUILayout.Button("보내기", GUILayout.Width(70f));
        GUILayout.EndHorizontal();

        IsTyping = GUI.GetNameOfFocusedControl() == ControlName;

        GUILayout.EndArea();

        if (sent)
        {
            Submit();
            GUI.FocusControl(null);
            IsTyping = false;
        }

        if (enterPressed && focused)
        {
            Submit();
            Event.current.Use();
        }
    }

    void Submit()
    {
        string text = draft.Trim();
        draft = "";
        scroll.y = float.MaxValue;

        if (text.Length > 0)
        {
            SubmitChatRpc(NetText.Trim512(text));
        }
    }

    [Rpc(SendTo.Server)]
    void SubmitChatRpc(FixedString512Bytes message, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var sender = new FixedString64Bytes("Unknown");

        if (NetworkManager.ConnectedClients.TryGetValue(senderId, out var client) && client.PlayerObject != null)
        {
            var avatar = client.PlayerObject.GetComponent<PlayerAvatar>();
            if (avatar != null)
            {
                sender = avatar.Nickname.Value;
            }
        }

        BroadcastChatRpc(sender, message);
    }

    [Rpc(SendTo.Everyone)]
    void BroadcastChatRpc(FixedString64Bytes sender, FixedString512Bytes message)
    {
        AddLine($"<b>{sender}</b>: {message}");
    }

    void AddLine(string line)
    {
        lines.Add(line);
        if (lines.Count > MaxLines)
        {
            lines.RemoveAt(0);
        }
        scroll.y = float.MaxValue;
    }

    static GUIStyle chatLabel;

    static GUIStyle ChatLabel()
    {
        chatLabel ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        return chatLabel;
    }
}
