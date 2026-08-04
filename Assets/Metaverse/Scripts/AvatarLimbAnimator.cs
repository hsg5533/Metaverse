using UnityEngine;

/// <summary>
/// Swings arms and legs based on how fast the avatar is actually moving.
/// Driven by observed position change, so remote avatars animate too without extra syncing.
/// Also plays the one-shot attack swing and the flinch when the avatar is hit.
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

    public float AttackDuration = 0.35f;
    public float HitDuration = 0.3f;

    Vector3 lastPosition;
    float phase;
    float rigBaseHeight;
    float attackEndTime;
    float hitEndTime;

    /// <summary>Raises the sword arm and chops it down once.</summary>
    public void PlayAttack()
    {
        attackEndTime = Time.time + AttackDuration;
    }

    /// <summary>Leans the body back for a moment after taking damage.</summary>
    public void PlayHit()
    {
        hitEndTime = Time.time + HitDuration;
    }

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
        SetPitch(LeftLeg, swing);
        SetPitch(RightLeg, -swing);

        // The sword arm follows the walk cycle unless an attack is playing on top of it.
        SetPitch(RightArm, TryGetAttackPitch(out float attackPitch) ? attackPitch : swing);

        if (Rig != null)
        {
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * intensity;
            Vector3 local = Rig.localPosition;
            local.y = rigBaseHeight + bob;
            Rig.localPosition = local;
            Rig.localRotation = Quaternion.Euler(HitLean(), 0f, 0f);
        }
    }

    /// <summary>Windup over the shoulder, then a chop forward.</summary>
    bool TryGetAttackPitch(out float pitch)
    {
        pitch = 0f;
        float remaining = attackEndTime - Time.time;
        if (remaining <= 0f)
        {
            return false;
        }

        const float windupPart = 0.35f;
        float progress = 1f - remaining / AttackDuration;

        pitch = progress < windupPart
            ? Mathf.Lerp(0f, -150f, Mathf.SmoothStep(0f, 1f, progress / windupPart))
            : Mathf.Lerp(-150f, -40f, Mathf.SmoothStep(0f, 1f, (progress - windupPart) / (1f - windupPart)));

        return true;
    }

    float HitLean()
    {
        float remaining = hitEndTime - Time.time;
        if (remaining <= 0f)
        {
            return 0f;
        }

        float progress = 1f - remaining / HitDuration;
        return -22f * Mathf.Sin(progress * Mathf.PI);
    }

    static void SetPitch(Transform limb, float degrees)
    {
        if (limb != null)
        {
            limb.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }
}
