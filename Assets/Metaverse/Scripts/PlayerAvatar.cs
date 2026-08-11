using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Networked avatar: the owner drives movement, the server owns the nickname and colour.
/// Position replication is handled by the NetworkTransform on the same prefab (owner authority).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerAvatar : NetworkBehaviour
{
    /// <summary>The avatar this client controls, used by shops and warp pads.</summary>
    public static PlayerAvatar Local;

    static readonly Color[] Palette =
    {
        new Color(0.90f, 0.32f, 0.30f),
        new Color(0.30f, 0.62f, 0.92f),
        new Color(0.36f, 0.80f, 0.46f),
        new Color(0.96f, 0.75f, 0.25f),
        new Color(0.72f, 0.44f, 0.90f),
        new Color(0.24f, 0.82f, 0.80f),
        new Color(0.95f, 0.55f, 0.75f),
        new Color(0.60f, 0.60f, 0.65f),
    };

    /// <summary>What hair can be, in the same order the mirror lists it.</summary>
    static readonly Color[] HairPalette =
    {
        new Color(0.13f, 0.11f, 0.10f),
        new Color(0.35f, 0.22f, 0.13f),
        new Color(0.60f, 0.40f, 0.22f),
        new Color(0.90f, 0.80f, 0.45f),
        new Color(0.86f, 0.86f, 0.88f),
        new Color(0.78f, 0.24f, 0.18f),
        new Color(0.32f, 0.52f, 0.86f),
        new Color(0.88f, 0.46f, 0.72f),
    };

    /// <summary>What skin can be, in the same order the mirror lists it: palest first, darkest last.</summary>
    static readonly Color[] SkinPalette =
    {
        new Color(0.99f, 0.86f, 0.74f),
        new Color(0.95f, 0.79f, 0.66f),
        new Color(0.90f, 0.70f, 0.55f),
        new Color(0.80f, 0.58f, 0.42f),
        new Color(0.68f, 0.48f, 0.34f),
        new Color(0.55f, 0.38f, 0.26f),
        new Color(0.42f, 0.28f, 0.18f),
        new Color(0.30f, 0.20f, 0.14f),
    };

    public static int SwatchCount => Palette.Length;

    public static int SkinPaletteCount => SkinPalette.Length;

    public static Color Swatch(int index) => Palette[Mathf.Clamp(index, 0, Palette.Length - 1)];

    public static Color HairSwatch(int index) => HairPalette[Mathf.Clamp(index, 0, HairPalette.Length - 1)];

    public static Color SkinSwatch(int index) => SkinPalette[Mathf.Clamp(index, 0, SkinPalette.Length - 1)];

    /// <summary>Body parts tinted with the player colour (shirt and arms).</summary>
    public Renderer[] ColoredParts;

    /// <summary>Legs, which take a colour of their own.</summary>
    public Renderer[] TrouserParts;

    /// <summary>The head, tinted with the skin colour.</summary>
    public Renderer[] SkinnedParts;

    /// <summary>One head of hair per style, only ever one of them switched on. Zero is bald.</summary>
    public GameObject[] HairStyles;


    /// <summary>Local height the name tag is drawn at.</summary>
    public float NameTagHeight = 2.3f;

    public float MoveSpeed = 5f;
    public float RunMultiplier = 1.8f;
    public float JumpHeight = 1.3f;
    public float Gravity = -22f;


    public NetworkVariable<FixedString64Bytes> Nickname =
        new(new FixedString64Bytes("Player"), writePerm: NetworkVariableWritePermission.Server);

    /// <summary>
    /// What the avatar looks like, as places in the palettes rather than colours: the server
    /// only has to clamp an index to know a client has not invented a colour of its own, and a
    /// save file holds four small numbers instead of four strings nobody can read.
    /// </summary>
    public NetworkVariable<int> BodyTint = new(0, writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> PantsTint = new(7, writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> HairTint = new(0, writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> HairStyle = new(1, writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> SkinTint = new(0, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>Everything the mirror sets, so subscribing to them is one line instead of five.</summary>
    NetworkVariable<int>[] LookChoices()
    {
        return new[] { BodyTint, PantsTint, HairTint, HairStyle, SkinTint };
    }

    CharacterController controller;
    PlayerBuffs buffs;
    PlayerFishing fishing;
    PlayerStats stats;

    // The name tag is drawn several times a frame, once per avatar on screen. Reading the
    // networked name gives a fresh string every time, which is a bagful of garbage for a line
    // that changes when somebody joins.
    string tag;
    FixedString64Bytes taggedName;
    float verticalVelocity;
    Vector3 lastStepPosition;
    float stepDistance;
    bool airborne;
    static GUIStyle nameTagStyle;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        buffs = GetComponent<PlayerBuffs>();
        fishing = GetComponent<PlayerFishing>();
        stats = GetComponent<PlayerStats>();
    }

    public override void OnNetworkSpawn()
    {
        foreach (var choice in LookChoices())
        {
            choice.OnValueChanged += OnLookChanged;
        }

        ApplyLook();

        if (IsServer)
        {
            // Something different per player until they pick for themselves at the mirror.
            BodyTint.Value = (int)(OwnerClientId % (ulong)Palette.Length);
            HairTint.Value = (int)(OwnerClientId % (ulong)HairPalette.Length);
            Nickname.Value = NetText.Trim64("플레이어 " + OwnerClientId);
        }

        if (IsOwner)
        {
            Local = this;

            // Statics survive a play session when domain reload is off; start clean.
            ShopNpc.PanelOpen = false;
            PlayerInventory.WindowOpen = false;
            PlayerStats.WindowOpen = false;
            Teleport(SpawnPointFor(OwnerClientId));
            SubmitNicknameRpc(NetText.Trim64(MetaverseHUD.LocalNickname));
            if (FollowCamera.Instance != null)
            {
                FollowCamera.Instance.Target = transform;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        foreach (var choice in LookChoices())
        {
            choice.OnValueChanged -= OnLookChanged;
        }
        if (Local == this)
        {
            Local = null;
        }
    }

    void Update()
    {
        Footsteps();

        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        var keyboard = Keyboard.current;
        bool acceptInput = keyboard != null && !ChatSystem.IsTyping;

        // A cast line pins you in place: reel in first, which any click does.
        bool lineOut = fishing != null && fishing.Casting;

        Vector2 input = lineOut ? Vector2.zero : MobileInput.Move;
        if (acceptInput && !lineOut)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        }

        float cameraYaw = FollowCamera.Instance != null ? FollowCamera.Instance.Yaw : transform.eulerAngles.y;
        Vector3 direction = Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(input.x, 0f, input.y);
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        if (controller.isGrounded)
        {
            // Coming down from a jump or a fall, not from walking off a step.
            if (airborne && verticalVelocity < -4f)
            {
                GameSound.Play(GameSound.Land, transform.position);
            }

            airborne = false;
            verticalVelocity = -2f;

            if ((acceptInput && keyboard.spaceKey.wasPressedThisFrame) || MobileInput.ConsumeJump())
            {
                verticalVelocity = Mathf.Sqrt(-2f * Gravity * JumpHeight);
                airborne = true;
                GameSound.Play(GameSound.Jump, transform.position);
            }
        }
        else
        {
            airborne = true;
            verticalVelocity += Gravity * Time.deltaTime;
        }

        float speed = acceptInput && keyboard.leftShiftKey.isPressed ? MoveSpeed * RunMultiplier : MoveSpeed;
        if (buffs != null)
        {
            speed *= buffs.SpeedMultiplier;
        }
        controller.Move((direction * speed + Vector3.up * verticalVelocity) * Time.deltaTime);

        if (direction.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 12f * Time.deltaTime);
        }

        if (transform.position.y < -20f)
        {
            Teleport(SpawnPointFor(OwnerClientId));
        }
    }

    /// <summary>
    /// Runs for every avatar, not just the owned one: the position is replicated, so each
    /// client can work out the steps of everybody it can see without a single message.
    /// </summary>
    void Footsteps()
    {
        var flat = new Vector3(transform.position.x, 0f, transform.position.z);
        stepDistance += Vector3.Distance(flat, lastStepPosition);
        lastStepPosition = flat;

        if (stepDistance >= 2.2f)
        {
            stepDistance = 0f;
            GameSound.Play(GameSound.Step, transform.position);
        }
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();
        GUI.depth = MetaverseUi.WorldDepth;

        if (!IsSpawned)
        {
            return;
        }

        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 screenPoint = MetaverseUi.ScreenPoint(camera, transform.position + Vector3.up * NameTagHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        nameTagStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,

            // Never measured, never clipped: a cached style and the font it ends up drawn
            // with do not always agree on how wide a name is.
            clipping = TextClipping.Overflow,
        };

        var rect = new Rect(screenPoint.x - 100f, MetaverseUi.Height - screenPoint.y - 20f, 200f, 20f);
        float barTop = rect.yMax;

        Color previous = GUI.color;
        GUI.color = IsOwner ? new Color(1f, 0.92f, 0.4f) : Color.white;
        if (tag == null || !taggedName.Equals(Nickname.Value))
        {
            taggedName = Nickname.Value;
            tag = taggedName.ToString();
        }

        GUI.Label(rect, tag, nameTagStyle);
        GUI.color = previous;

        if (stats != null && stats.MaxHp > 0)
        {
            const float barWidth = 70f;
            MetaverseUi.Bar(new Rect(screenPoint.x - barWidth * 0.5f, barTop, barWidth, 5f),
                stats.Hp.Value / (float)stats.MaxHp, new Color(0.35f, 0.80f, 0.40f, 0.95f));
        }
    }

    [Rpc(SendTo.Server)]
    void SubmitNicknameRpc(FixedString64Bytes nickname)
    {
        if (nickname.Length > 0)
        {
            Nickname.Value = nickname;
            // The nickname is the save key, so this is the first moment progress can be restored.
            SaveSystem.LoadInto(this);
        }
    }

    void OnLookChanged(int previous, int current)
    {
        ApplyLook();
    }

    /// <summary>
    /// Server side: the mirror asking for a look. Every choice is a place in a palette, so
    /// clamping it is the whole of the checking that needs doing.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void SetLookRpc(int body, int pants, int hair, int style, int skin, RpcParams rpcParams = default)
    {
        if (this.IsFromOwner(rpcParams))
        {
            SetLook(body, pants, hair, style, skin);
        }
    }

    /// <summary>Server side: the same, for a look coming back out of the save file.</summary>
    public void SetLook(int body, int pants, int hair, int style, int skin)
    {
        if (!IsServer)
        {
            return;
        }

        BodyTint.Value = Mathf.Clamp(body, 0, Palette.Length - 1);
        PantsTint.Value = Mathf.Clamp(pants, 0, Palette.Length - 1);
        HairTint.Value = Mathf.Clamp(hair, 0, HairPalette.Length - 1);
        HairStyle.Value = HairStyles != null ? Mathf.Clamp(style, 0, HairStyles.Length - 1) : 0;
        SkinTint.Value = Mathf.Clamp(skin, 0, SkinPalette.Length - 1);
    }

    /// <summary>Shirt, trousers, skin, and whichever head of hair is being worn.</summary>
    void ApplyLook()
    {
        ApplyColor(ColoredParts, Swatch(BodyTint.Value));
        ApplyColor(TrouserParts, Swatch(PantsTint.Value));
        ApplyColor(SkinnedParts, SkinSwatch(SkinTint.Value));

        if (HairStyles == null)
        {
            return;
        }

        for (int i = 0; i < HairStyles.Length; i++)
        {
            if (HairStyles[i] == null)
            {
                continue;
            }

            bool worn = i == HairStyle.Value;
            HairStyles[i].SetActive(worn);
            if (worn)
            {
                ApplyColor(HairStyles[i].GetComponentsInChildren<Renderer>(true), HairSwatch(HairTint.Value));
            }
        }
    }

    static MaterialPropertyBlock colorBlock;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    static void ApplyColor(Renderer[] parts, Color color)
    {
        if (parts == null)
        {
            return;
        }

        // A property block instead of renderer.material, which would clone the material and
        // take the part out of every batch it was in.
        colorBlock ??= new MaterialPropertyBlock();
        colorBlock.SetColor(BaseColor, color);

        foreach (var part in parts)
        {
            if (part != null)
            {
                part.SetPropertyBlock(colorBlock);
            }
        }
    }

    /// <summary>Moves the avatar without interpolating, so a warp does not slide across the map.</summary>
    public void Teleport(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        verticalVelocity = 0f;

        // A warp is not a stride: without this the jump across the map counts as one.
        lastStepPosition = new Vector3(position.x, 0f, position.z);
        stepDistance = 0f;
        GameSound.Play(GameSound.Warp, position);

        var networkTransform = GetComponent<NetworkTransform>();
        if (IsSpawned && networkTransform != null)
        {
            networkTransform.Teleport(position, transform.rotation, transform.localScale);
        }
    }

    /// <summary>Village spawn ring, also used when a monster knocks the avatar out.</summary>
    public static Vector3 SpawnPointFor(ulong clientId)
    {
        float angle = clientId * 47f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle) * 7f, 0.6f, Mathf.Sin(angle) * 7f);
    }
}
