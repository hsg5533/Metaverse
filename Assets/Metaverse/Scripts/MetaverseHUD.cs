using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Connect menu and in-session status panel, drawn with IMGUI so no UI prefabs are needed.
/// </summary>
public class MetaverseHUD : MonoBehaviour
{
    /// <summary>Nickname typed in the connect menu, sent to the server once the avatar spawns.</summary>
    public static string LocalNickname = "Player";

    string address = "127.0.0.1";
    string port = "7777";
    string message = "";

    void Awake()
    {
        LocalNickname = "Player" + Random.Range(100, 1000);
    }

    void Start()
    {
        ApplyCommandLine();
    }

    /// <summary>
    /// Lets a build start without clicking: -mvhost, -mvclient &lt;address&gt;, -mvnick &lt;name&gt;.
    /// Useful for a dedicated server and for testing several clients at once.
    /// </summary>
    void ApplyCommandLine()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        string[] args = System.Environment.GetCommandLineArgs();
        bool host = false;
        bool client = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-mvhost":
                    host = true;
                    break;
                case "-mvclient":
                    client = true;
                    if (i + 1 < args.Length) address = args[i + 1];
                    break;
                case "-mvnick":
                    if (i + 1 < args.Length) LocalNickname = args[i + 1];
                    break;
                case "-mvport":
                    if (i + 1 < args.Length) port = args[i + 1];
                    break;
            }
        }

        if (host || client)
        {
            manager.OnClientConnectedCallback += id => Debug.Log($"[Metaverse] client {id} connected");
            manager.OnClientDisconnectCallback += id => Debug.Log($"[Metaverse] client {id} disconnected");
            Connect(manager, host);
            Debug.Log($"[Metaverse] auto start as {(host ? "host" : "client")} -> {address}:{port}");
        }
    }

    void OnGUI()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 250, 230), GUI.skin.box);
        if (manager.IsClient || manager.IsServer)
        {
            DrawSessionPanel(manager);
        }
        else
        {
            DrawConnectMenu(manager);
        }
        GUILayout.EndArea();
    }

    void DrawConnectMenu(NetworkManager manager)
    {
        GUILayout.Label("<b>Metaverse</b>", RichLabel());

        GUILayout.Label("Nickname");
        LocalNickname = GUILayout.TextField(LocalNickname, 16);

        GUILayout.Label("Server address");
        GUILayout.BeginHorizontal();
        address = GUILayout.TextField(address, 40);
        port = GUILayout.TextField(port, 5, GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        if (GUILayout.Button("Host (play + serve)"))
        {
            Connect(manager, host: true);
        }
        if (GUILayout.Button("Join as client"))
        {
            Connect(manager, host: false);
        }

        if (!string.IsNullOrEmpty(message))
        {
            GUILayout.Label(message);
        }
    }

    void DrawSessionPanel(NetworkManager manager)
    {
        string role = manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client";
        GUILayout.Label($"<b>{role}</b> as {LocalNickname}", RichLabel());

        if (manager.IsServer)
        {
            GUILayout.Label($"Players online: {manager.ConnectedClientsIds.Count}");
        }
        else
        {
            GUILayout.Label(manager.IsConnectedClient ? "Connected" : "Connecting...");
        }

        GUILayout.Space(4);
        GUILayout.Label("WASD move, Space jump, Shift run");
        GUILayout.Label("Right mouse drag looks around");
        GUILayout.Label("Left click attacks, E interacts");
        GUILayout.Label("T trade, Z wave, X dance, C sit");

        GUILayout.Space(6);
        if (GUILayout.Button("Leave"))
        {
            manager.Shutdown();
            message = "";
        }
    }

    void Connect(NetworkManager manager, bool host)
    {
        if (!ushort.TryParse(port, out ushort parsedPort))
        {
            message = "Port must be a number.";
            return;
        }

        var transport = manager.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(host ? "0.0.0.0" : address.Trim(), parsedPort, host ? "0.0.0.0" : null);
        }

        // Binding fails after StartHost has already returned true, so the failure only shows
        // up through this event. Without it the menu just sits there looking like nothing happened.
        manager.OnTransportFailure -= OnTransportFailure;
        manager.OnTransportFailure += OnTransportFailure;

        bool started = host ? manager.StartHost() : manager.StartClient();
        message = started ? "" : $"Could not start on port {parsedPort}.";
    }

    void OnTransportFailure()
    {
        message = $"Port {port} is busy. Try another port, or restart Unity.";
        Debug.LogWarning($"[Metaverse] transport failed to start on port {port}.");
    }

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
