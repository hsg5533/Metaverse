using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third person orbit camera. The local avatar assigns itself as the target when it spawns.
/// </summary>
public class FollowCamera : MonoBehaviour
{
    public static FollowCamera Instance;

    public Transform Target;
    public float Distance = 6f;
    public float FocusHeight = 1.5f;
    public float Sensitivity = 0.15f;

    /// <summary>How much harder a finger drag turns the camera than a mouse drag.</summary>
    public float TouchSensitivity = 3f;

    float yaw;
    float pitch = 15f;

    /// <summary>Current horizontal camera angle, used to make movement camera relative.</summary>
    public float Yaw => yaw;

    void Awake()
    {
        Instance = this;
    }

    void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * Sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * Sensitivity, -20f, 70f);
        }

        // A thumb travels a fraction of what a mouse does, and the drag arrives already
        // divided by the interface scale, so it needs a good deal more per pixel.
        Vector2 drag = MobileInput.Look * TouchSensitivity;
        if (drag.sqrMagnitude > 0f)
        {
            yaw += drag.x * Sensitivity;
            pitch = Mathf.Clamp(pitch - drag.y * Sensitivity, -20f, 70f);
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = Target.position + Vector3.up * FocusHeight;
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * Distance, rotation);
    }
}
