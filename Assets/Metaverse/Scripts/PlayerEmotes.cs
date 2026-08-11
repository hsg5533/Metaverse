using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Wave, dance, sit. The pose is a server owned number, so everyone sees the same thing,
/// and any movement input drops it again.
/// </summary>
public class PlayerEmotes : NetworkBehaviour
{
    public const int None = 0;
    public const int Wave = 1;
    public const int Dance = 2;
    public const int Sit = 3;

    public NetworkVariable<int> Emote = new(None, writePerm: NetworkVariableWritePermission.Server);

    AvatarLimbAnimator limbAnimator;

    void Awake()
    {
        limbAnimator = GetComponent<AvatarLimbAnimator>();
    }

    void Update()
    {
        if (limbAnimator != null)
        {
            limbAnimator.Emote = Emote.Value;
        }

        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null || ChatSystem.IsTyping || ShopNpc.PanelOpen)
        {
            return;
        }

        if (keyboard.zKey.wasPressedThisFrame || MobileInput.Pressed(Key.Z))
        {
            SetRpc(Emote.Value == Wave ? None : Wave);
        }
        else if (keyboard.xKey.wasPressedThisFrame || MobileInput.Pressed(Key.X))
        {
            SetRpc(Emote.Value == Dance ? None : Dance);
        }
        else if (keyboard.cKey.wasPressedThisFrame || MobileInput.Pressed(Key.C))
        {
            SetRpc(Emote.Value == Sit ? None : Sit);
        }
        else if (Emote.Value != None && Moving(keyboard))
        {
            SetRpc(None);
        }
    }

    /// <summary>Whatever else the player does, they are no longer sitting still.</summary>
    public void Stop()
    {
        if (IsOwner && IsSpawned && Emote.Value != None)
        {
            SetRpc(None);
        }
    }

    static bool Moving(Keyboard keyboard)
    {
        // The stick counts too, or a phone has no way to stand back up.
        return MobileInput.Move.sqrMagnitude > 0.05f
            || keyboard.wKey.isPressed
            || keyboard.aKey.isPressed
            || keyboard.sKey.isPressed
            || keyboard.dKey.isPressed
            || keyboard.upArrowKey.isPressed
            || keyboard.downArrowKey.isPressed
            || keyboard.leftArrowKey.isPressed
            || keyboard.rightArrowKey.isPressed
            || keyboard.spaceKey.isPressed;
    }

    [Rpc(SendTo.Server)]
    void SetRpc(int emote, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == OwnerClientId)
        {
            Emote.Value = Mathf.Clamp(emote, None, Sit);
        }
    }
}
