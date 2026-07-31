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

    const string ControlName = "metaverseChatInput";
    const int MaxLines = 12;

    readonly List<string> lines = new();
    string draft = "";
    Vector2 scroll;

    public override void OnNetworkSpawn()
    {
        lines.Clear();
        AddLine("<i>Press Enter to chat.</i>");
    }

    public override void OnNetworkDespawn()
    {
        IsTyping = false;
    }

    void OnGUI()
    {
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

        var area = new Rect(10, Screen.height - 210, 380, 200);
        GUILayout.BeginArea(area, GUI.skin.box);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        foreach (string line in lines)
        {
            GUILayout.Label(line, ChatLabel());
        }
        GUILayout.EndScrollView();

        GUI.SetNextControlName(ControlName);
        draft = GUILayout.TextField(draft, 120);
        IsTyping = GUI.GetNameOfFocusedControl() == ControlName;

        GUILayout.EndArea();

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
