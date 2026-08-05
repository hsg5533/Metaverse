using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Connect menu and in-session status panel, drawn with IMGUI so no UI prefabs are needed.
/// </summary>
public class MetaverseHUD : MonoBehaviour
{
    /// <summary>Nickname typed in the connect menu, sent to the server once the avatar spawns.</summary>
    public static string LocalNickname = "Player";

    /// <summary>True while the cursor sits on the help button or its panel, so clicking it does not swing.</summary>
    public static bool PointerOverHud;

    /// <summary>Every control, listed in the panel behind the gear.</summary>
    static readonly (string Keys, string What)[] Controls =
    {
        ("WASD / 방향키", "이동"),
        ("Shift", "달리기"),
        ("Space", "점프"),
        ("마우스 좌클릭", "공격"),
        ("E", "상점 · 모루 · 모닥불 · 게시판 · 채집 · 워프"),
        ("P", "캐릭터 정보"),
        ("I", "인벤토리"),
        ("T / Y", "거래 신청 / 수락"),
        ("G / H", "아레나 결투 신청 / 수락"),
        ("O / U / L", "파티 초대 / 수락 / 나가기"),
        ("Z X C", "인사 · 춤 · 앉기"),
        ("우클릭 드래그", "시점 회전, 휠 확대"),
        ("Enter", "채팅"),
        ("Esc", "창 닫기 · 게임 종료"),
    };

    bool helpOpen;

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

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || ChatSystem.IsTyping)
        {
            return;
        }

        // Escape backs out of whatever is open first; only a clear screen means quit.
        if (PlayerInventory.WindowOpen || ShopNpc.PanelOpen || helpOpen)
        {
            helpOpen = false;
            return;
        }

        Quit();
    }

    /// <summary>Leaves the session cleanly so the save is written, then closes the game.</summary>
    void Quit()
    {
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            manager.Shutdown();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        MetaverseUi.ApplyFont();

        var manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        // The connect menu has fields and two buttons; the session panel is three lines.
        bool connected = manager.IsClient || manager.IsServer;
        float height = connected ? 130f : 230f;

        GUILayout.BeginArea(new Rect(10, 10, 250, height), GUI.skin.box);
        if (connected)
        {
            DrawSessionPanel(manager);
        }
        else
        {
            DrawConnectMenu(manager);
        }
        GUILayout.EndArea();

        DrawHelp();
    }

    /// <summary>
    /// The gear in the bottom right corner and the list it opens. The gear is drawn from
    /// rotated rectangles, so it needs no texture and no font that has the glyph.
    /// </summary>
    void DrawHelp()
    {
        const float size = 44f;
        var button = new Rect(Screen.width - size - 14f, Screen.height - size - 14f, size, size);
        var panel = new Rect(Screen.width - 366f, Screen.height - 66f - Controls.Length * 20f, 352f, Controls.Length * 20f + 34f);

        Vector2 pointer = Event.current.mousePosition;
        PointerOverHud = button.Contains(pointer) || (helpOpen && panel.Contains(pointer));

        if (helpOpen)
        {
            GUI.Box(panel, "");
            GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, panel.width, 20f), "<b>조작</b>", RichLabel());

            for (int i = 0; i < Controls.Length; i++)
            {
                float y = panel.y + 28f + i * 20f;
                GUI.Label(new Rect(panel.x + 12f, y, 110f, 20f), Controls[i].Keys);
                GUI.Label(new Rect(panel.x + 128f, y, panel.width - 140f, 20f), Controls[i].What);
            }
        }

        if (GUI.Button(button, GUIContent.none))
        {
            helpOpen = !helpOpen;
        }

        DrawGear(button.center, size * 0.32f, helpOpen ? new Color(1f, 0.92f, 0.4f) : Color.white);
    }

    static void DrawGear(Vector2 centre, float radius, Color colour)
    {
        Color previousColour = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.color = colour;

        // Eight teeth around the rim.
        for (int i = 0; i < 8; i++)
        {
            GUI.DrawTexture(new Rect(centre.x - 2.5f, centre.y - radius - 5f, 5f, 7f), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(45f, centre);
        }

        GUI.matrix = previousMatrix;

        // Body: a square plus a turned square makes a passable disc at this size.
        DrawOctagon(centre, radius, colour);
        DrawOctagon(centre, radius * 0.42f, new Color(0.16f, 0.16f, 0.18f));

        GUI.color = previousColour;
        GUI.matrix = previousMatrix;
    }

    static void DrawOctagon(Vector2 centre, float radius, Color colour)
    {
        Color previousColour = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.color = colour;

        for (int i = 0; i < 2; i++)
        {
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(45f, centre);
        }

        GUI.matrix = previousMatrix;
        GUI.color = previousColour;
    }

    void DrawConnectMenu(NetworkManager manager)
    {
        GUILayout.Label("<b>메타버스</b>", RichLabel());

        GUILayout.Label("닉네임");
        LocalNickname = GUILayout.TextField(LocalNickname, 16);

        GUILayout.Label("서버 주소");
        GUILayout.BeginHorizontal();
        address = GUILayout.TextField(address, 40);
        port = GUILayout.TextField(port, 5, GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        if (GUILayout.Button("호스트로 시작"))
        {
            Connect(manager, host: true);
        }
        if (GUILayout.Button("클라이언트로 접속"))
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
        string role = manager.IsHost ? "호스트" : manager.IsServer ? "서버" : "클라이언트";
        GUILayout.Label($"<b>{role}</b> · {LocalNickname}", RichLabel());

        if (manager.IsServer)
        {
            GUILayout.Label($"접속 인원: {manager.ConnectedClientsIds.Count}");
        }
        else
        {
            GUILayout.Label(manager.IsConnectedClient ? "접속됨" : "접속 중...");
        }

        GUILayout.Space(6);
        if (GUILayout.Button("나가기"))
        {
            manager.Shutdown();
            message = "";
        }
    }

    void Connect(NetworkManager manager, bool host)
    {
        if (!ushort.TryParse(port, out ushort parsedPort))
        {
            message = "포트는 숫자여야 합니다.";
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
        message = started ? "" : $"포트 {parsedPort}에서 시작하지 못했습니다.";
    }

    void OnTransportFailure()
    {
        message = $"포트 {port}가 사용 중입니다. 다른 포트를 쓰거나 Unity를 재시작하세요.";
        Debug.LogWarning($"[Metaverse] transport failed to start on port {port}.");
    }

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
