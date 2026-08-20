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
        ("G / H", "아레나 결투 신청 / 수락"),
        ("O / U / L", "파티 초대 / 수락 / 나가기"),
        ("Z X C", "인사 · 춤 · 앉기"),
        ("우클릭 드래그", "시점 회전"),
        ("Enter", "채팅"),
        ("Esc", "창 닫기 · 게임 종료"),
    };

    /// <summary>
    /// What the gear opens on a touch screen: the same shortcuts as buttons. A list of key
    /// hints is useless without a keyboard, so on a phone it becomes the menu.
    /// </summary>
    static readonly (string What, Key Key)[] TouchActions =
    {
        ("인벤토리", Key.I),
        ("캐릭터 정보", Key.P),
        ("파티 초대", Key.O),
        ("파티 수락", Key.U),
        ("파티 나가기", Key.L),
        ("결투 신청", Key.G),
        ("결투 수락", Key.H),
        ("인사", Key.Z),
        ("춤", Key.X),
        ("앉기", Key.C),
    };

    /// <summary>True while the gear panel is up, so the touch controls under it stay quiet.</summary>
    public static bool MenuOpen;

    bool helpOpen;

    string address = "127.0.0.1";
    string port = "7777";
    string message = "";

    const string NicknamePrefKey = "Metaverse.LocalNickname";

    void Awake()
    {
        // Statics outlive a play session when domain reload is off.
        MenuOpen = false;
        LocalNickname = PlayerPrefs.GetString(NicknamePrefKey, "Player" + Random.Range(100, 1000));
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
                    if (i + 1 < args.Length)
                        address = args[i + 1];
                    break;
                case "-mvnick":
                    if (i + 1 < args.Length)
                        LocalNickname = args[i + 1];
                    break;
                case "-mvport":
                    if (i + 1 < args.Length)
                        port = args[i + 1];
                    break;
            }
        }

        if (host || client)
        {
            manager.OnClientConnectedCallback += id =>
                Debug.Log($"[Metaverse] client {id} connected");
            manager.OnClientDisconnectCallback += id =>
                Debug.Log($"[Metaverse] client {id} disconnected");
            Connect(manager, host);
            Debug.Log(
                $"[Metaverse] auto start as {(host ? "host" : "client")} -> {address}:{port}"
            );
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

        // Connection info and the exit button used to sit in an always-on panel; now they only
        // show up behind the gear, alongside the controls list, so the connect menu is the only
        // thing left permanently on screen.
        bool connected = manager.IsClient || manager.IsServer;
        if (!connected)
        {
            GUILayout.BeginArea(new Rect(10, 10, 250f, 230f), GUI.skin.box);
            DrawConnectMenu(manager);
            GUILayout.EndArea();
        }

        // One pass for every monster in the world; they used to each own an OnGUI.
        GUI.depth = MetaverseUi.WorldDepth;
        Monster.DrawTags();
        DamageNumbers.Draw();
        GUI.depth = 0;

        DrawHelp();
    }

    /// <summary>
    /// The gear in the bottom right corner and the list it opens. The gear is drawn from
    /// rotated rectangles, so it needs no texture and no font that has the glyph.
    /// </summary>
    void DrawHelp()
    {
        bool touch = MobileInput.Active;
        var manager = NetworkManager.Singleton;
        bool connected = manager != null && (manager.IsClient || manager.IsServer);

        // On a phone the gear sits top right: the bottom right corner is all thumb.
        float size = touch ? 60f : 44f;
        int rows = touch ? (TouchActions.Length + 1) / 2 : Controls.Length;
        float rowHeight = touch ? 40f : 20f;

        // The exit button rides at the bottom of the same panel, below the controls list,
        // only while connected - no other session details, just the one way out.
        float exitHeight = connected ? 34f : 0f;

        var button = touch
            ? new Rect(MetaverseUi.Width - size - 14f, 14f, size, size)
            : new Rect(MetaverseUi.Width - size - 14f, MetaverseUi.Height - size - 14f, size, size);

        float panelHeight = rows * rowHeight + 36f + exitHeight;
        var panel = touch
            ? new Rect(MetaverseUi.Width - 366f, button.yMax + 8f, 352f, panelHeight)
            : new Rect(
                MetaverseUi.Width - 366f,
                MetaverseUi.Height - 66f - panelHeight,
                352f,
                panelHeight
            );

        // Only meaningful for a mouse: it stops a click on the gear from also swinging the
        // sword. A touch leaves its last position behind after the finger lifts, which would
        // latch this on for good, and on a phone the attack comes from its own button anyway.
        Vector2 pointer = Event.current.mousePosition;
        PointerOverHud =
            !MobileInput.Active
            && (button.Contains(pointer) || (helpOpen && panel.Contains(pointer)));

        if (helpOpen)
        {
            GUI.Box(panel, "");
            GUI.Label(
                new Rect(panel.x + 10f, panel.y + 6f, panel.width, 20f),
                touch ? "<b>메뉴</b>" : "<b>조작</b>",
                MetaverseUi.Rich
            );

            if (touch)
            {
                // Two columns of buttons, each pressing the key it stands for.
                for (int i = 0; i < TouchActions.Length; i++)
                {
                    var cell = new Rect(
                        panel.x + 12f + (i % 2) * 168f,
                        panel.y + 28f + (i / 2) * rowHeight,
                        160f,
                        rowHeight - 6f
                    );
                    if (GUI.Button(cell, TouchActions[i].What))
                    {
                        MobileInput.Press(TouchActions[i].Key);
                        helpOpen = false;
                    }
                }
            }
            else
            {
                for (int i = 0; i < Controls.Length; i++)
                {
                    float y = panel.y + 28f + i * 20f;
                    GUI.Label(new Rect(panel.x + 12f, y, 110f, 20f), Controls[i].Keys);
                    GUI.Label(
                        new Rect(panel.x + 128f, y, panel.width - 140f, 20f),
                        Controls[i].What
                    );
                }
            }

            if (
                connected
                && GUI.Button(
                    new Rect(panel.x + 10f, panel.yMax - exitHeight + 4f, 100f, 24f),
                    "나가기"
                )
            )
            {
                manager.Shutdown();
                message = "";
                helpOpen = false;
            }
        }

        if (GUI.Button(button, GUIContent.none))
        {
            helpOpen = !helpOpen;
        }

        MenuOpen = helpOpen;

        DrawGear(button.center, size * 0.32f, helpOpen ? new Color(1f, 0.92f, 0.4f) : Color.white);
    }

    /// <summary>
    /// Eight teeth around a body, placed with sin and cos. Not by turning the GUI matrix:
    /// that matrix already carries the interface scale, and rotating it there sends the
    /// pieces flying across the screen.
    /// </summary>
    static void DrawGear(Vector2 centre, float radius, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = colour;

        float tooth = radius * 0.34f;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            GUI.DrawTexture(
                new Rect(
                    centre.x + Mathf.Cos(angle) * radius * 1.15f - tooth * 0.5f,
                    centre.y + Mathf.Sin(angle) * radius * 1.15f - tooth * 0.5f,
                    tooth,
                    tooth
                ),
                Texture2D.whiteTexture
            );
        }

        GUI.DrawTexture(
            new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f),
            Texture2D.whiteTexture
        );

        GUI.color = new Color(0.16f, 0.16f, 0.18f);
        float hole = radius * 0.42f;
        GUI.DrawTexture(
            new Rect(centre.x - hole, centre.y - hole, hole * 2f, hole * 2f),
            Texture2D.whiteTexture
        );

        GUI.color = previous;
    }

    void DrawConnectMenu(NetworkManager manager)
    {
        GUILayout.Label("<b>메타버스</b>", MetaverseUi.Rich);

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

    void Connect(NetworkManager manager, bool host)
    {
        if (!ushort.TryParse(port, out ushort parsedPort))
        {
            message = "포트는 숫자여야 합니다.";
            return;
        }

        PlayerPrefs.SetString(NicknamePrefKey, LocalNickname);

        var transport = manager.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(
                host ? "0.0.0.0" : address.Trim(),
                parsedPort,
                host ? "0.0.0.0" : null
            );
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
}
