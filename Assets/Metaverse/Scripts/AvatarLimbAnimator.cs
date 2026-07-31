using UnityEngine;

/// <summary>
/// Swings arms and legs based on how fast the avatar is actually moving.
/// Driven by observed position change, so remote avatars animate too without extra syncing.
/// </summary>
public class AvatarLimbAnimator : MonoBehaviour
{
    public Transform Rig;
    public Transform LeftArm;
    public Transform RightArm;
    public Transform LeftLeg;
    public Transform RightLeg;

    public float SwingDegrees = 45f;
    public float FullSwingSpeed = 5f;
    public float StepsPerMeter = 1.1f;

    Vector3 lastPosition;
    float phase;
    float rigBaseHeight;

    void Start()
    {
        lastPosition = transform.position;
        if (Rig != null)
        {
            rigBaseHeight = Rig.localPosition.y;
        }
    }

    void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;
        delta.y = 0f;

        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        float intensity = Mathf.Clamp01(speed / FullSwingSpeed);

        phase += delta.magnitude * StepsPerMeter * Mathf.PI * 2f;
        if (intensity < 0.01f)
        {
            phase = Mathf.Lerp(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, 10f * Time.deltaTime);
        }

        float swing = Mathf.Sin(phase) * SwingDegrees * intensity;
        SetPitch(LeftArm, -swing);
        SetPitch(RightArm, swing);
        SetPitch(LeftLeg, swing);
        SetPitch(RightLeg, -swing);

        if (Rig != null)
        {
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * intensity;
            Vector3 local = Rig.localPosition;
            local.y = rigBaseHeight + bob;
            Rig.localPosition = local;
        }
    }

    static void SetPitch(Transform limb, float degrees)
    {
        if (limb != null)
        {
            limb.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }
}
