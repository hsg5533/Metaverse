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

    /// <summary>Body parts tinted with the player colour (shirt and arms).</summary>
    public Renderer[] ColoredParts;


    /// <summary>Local height the name tag is drawn at.</summary>
    public float NameTagHeight = 2.3f;

    public float MoveSpeed = 5f;
    public float RunMultiplier = 1.8f;
    public float JumpHeight = 1.3f;
    public float Gravity = -22f;


    public NetworkVariable<FixedString64Bytes> Nickname =
        new(new FixedString64Bytes("Player"), writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<Color> BodyColor =
        new(Color.gray, writePerm: NetworkVariableWritePermission.Server);

    CharacterController controller;
    PlayerBuffs buffs;
    float verticalVelocity;
    Vector3 lastStepPosition;
    float stepDistance;
    bool airborne;
    static GUIStyle nameTagStyle;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        buffs = GetComponent<PlayerBuffs>();
    }

    public override void OnNetworkSpawn()
    {
        BodyColor.OnValueChanged += OnBodyColorChanged;
        ApplyColor(BodyColor.Value);


        if (IsServer)
        {
            BodyColor.Value = Palette[OwnerClientId % (ulong)Palette.Length];
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
        BodyColor.OnValueChanged -= OnBodyColorChanged;
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

        Vector2 input = Vector2.zero;
        if (acceptInput)
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

            if (acceptInput && keyboard.spaceKey.wasPressedThisFrame)
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

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * NameTagHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        nameTagStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };

        var content = new GUIContent(Nickname.Value.ToString());
        Vector2 size = nameTagStyle.CalcSize(content);
        var rect = new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y - size.y, size.x, size.y);

        Color previous = GUI.color;
        GUI.color = IsOwner ? new Color(1f, 0.92f, 0.4f) : Color.white;
        GUI.Label(rect, content, nameTagStyle);
        GUI.color = previous;

        var stats = GetComponent<PlayerStats>();
        if (stats != null && stats.MaxHp > 0)
        {
            const float barWidth = 70f;
            MetaverseUi.Bar(new Rect(screenPoint.x - barWidth * 0.5f, rect.yMax, barWidth, 5f),
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

    void OnBodyColorChanged(Color previous, Color current)
    {
        ApplyColor(current);
    }

    void ApplyColor(Color color)
    {
        if (ColoredParts == null)
        {
            return;
        }

        foreach (var part in ColoredParts)
        {
            if (part != null)
            {
                part.material.color = color;
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
