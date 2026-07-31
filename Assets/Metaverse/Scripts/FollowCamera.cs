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
        if (mouse != null)
        {
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * Sensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * Sensitivity, -20f, 70f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Distance = Mathf.Clamp(Distance - scroll * 0.01f, 2.5f, 14f);
            }
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = Target.position + Vector3.up * FocusHeight;
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * Distance, rotation);
    }
}
