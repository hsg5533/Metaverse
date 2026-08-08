using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One timed buff per kind, from cooking. Eating a stew and a tea both stay active at once, and
/// eating a second stew stacks its time on top of the first instead of resetting the clock. The
/// server sets each end time; clients compare that against the synchronised server clock, so no
/// ticking is replicated.
/// </summary>
public class PlayerBuffs : NetworkBehaviour
{
    public const int None = 0;
    public const int Attack = 1;
    public const int Defense = 2;
    public const int Speed = 3;

    public int AttackAmount = 6;
    public int DefenseAmount = 6;
    public float SpeedAmount = 1.4f;

    public NetworkVariable<double> AttackEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> DefenseEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> SpeedEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);

    public bool Active => RemainingOf(Attack) > 0f || RemainingOf(Defense) > 0f || RemainingOf(Speed) > 0f;

    public float RemainingOf(int kind)
    {
        if (!IsSpawned || NetworkManager == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)(EndTimeOf(kind) - NetworkManager.ServerTime.Time));
    }

    public int AttackBonus => RemainingOf(Attack) > 0f ? AttackAmount : 0;
    public int DefenseBonus => RemainingOf(Defense) > 0f ? DefenseAmount : 0;
    public float SpeedMultiplier => RemainingOf(Speed) > 0f ? SpeedAmount : 1f;

    /// <summary>
    /// Server side: stacks with whatever other kind is running, and with itself - a second
    /// dish of the same kind adds its time on top instead of resetting the clock.
    /// </summary>
    public void Apply(int kind, float seconds)
    {
        if (!IsServer)
        {
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        double end = System.Math.Max(EndTimeOf(kind), now) + seconds;

        switch (kind)
        {
            case Attack:
                AttackEndTime.Value = end;
                break;
            case Defense:
                DefenseEndTime.Value = end;
                break;
            case Speed:
                SpeedEndTime.Value = end;
                break;
        }
    }

    double EndTimeOf(int kind)
    {
        return kind switch
        {
            Attack => AttackEndTime.Value,
            Defense => DefenseEndTime.Value,
            Speed => SpeedEndTime.Value,
            _ => 0d,
        };
    }

    public static string NameOf(int kind)
    {
        return kind switch
        {
            Attack => "공격력 증가",
            Defense => "방어력 증가",
            Speed => "이동속도 증가",
            _ => "",
        };
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        float attack = RemainingOf(Attack);
        float defense = RemainingOf(Defense);
        float speed = RemainingOf(Speed);
        int count = (attack > 0f ? 1 : 0) + (defense > 0f ? 1 : 0) + (speed > 0f ? 1 : 0);
        if (count == 0)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(MetaverseUi.Width - 270, 238, 260, count * 20f + 8f), GUI.skin.box);
        DrawLine(Attack, attack);
        DrawLine(Defense, defense);
        DrawLine(Speed, speed);
        GUILayout.EndArea();
    }

    static void DrawLine(int kind, float remaining)
    {
        if (remaining > 0f)
        {
            GUILayout.Label($"{NameOf(kind)}   {Mathf.CeilToInt(remaining)}초 남음");
        }
    }
}
