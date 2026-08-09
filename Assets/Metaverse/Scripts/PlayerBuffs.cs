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
    public const int AttackSpeed = 4;

    /// <summary>Every kind but None, in buff-panel order - the one list everything else loops over.</summary>
    public static readonly int[] Kinds = { Attack, Defense, Speed, AttackSpeed };

    static readonly string[] Names = { "", "공격력 증가", "방어력 증가", "이동속도 증가", "공격속도 증가" };

    public int AttackAmount = 6;
    public int DefenseAmount = 6;
    public float SpeedAmount = 1.4f;
    public float AttackSpeedAmount = 1.4f;

    public NetworkVariable<double> AttackEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> DefenseEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> SpeedEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> AttackSpeedEndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);

    // Netcode only auto-syncs a NetworkVariable declared as its own field, so the four above stay
    // separate; this is just an index into them (None unused) for the shared lookup/apply code.
    NetworkVariable<double>[] endTimes;

    void Awake()
    {
        endTimes = new[] { null, AttackEndTime, DefenseEndTime, SpeedEndTime, AttackSpeedEndTime };
    }

    public bool Active
    {
        get
        {
            foreach (int kind in Kinds)
            {
                if (RemainingOf(kind) > 0f)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
    public float AttackSpeedMultiplier => RemainingOf(AttackSpeed) > 0f ? AttackSpeedAmount : 1f;

    /// <summary>
    /// Server side: stacks with whatever other kind is running, and with itself - a second
    /// dish of the same kind adds its time on top instead of resetting the clock.
    /// </summary>
    public void Apply(int kind, float seconds)
    {
        if (!IsServer || kind <= None || kind >= endTimes.Length)
        {
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        endTimes[kind].Value = System.Math.Max(endTimes[kind].Value, now) + seconds;
    }

    double EndTimeOf(int kind)
    {
        return kind > None && kind < endTimes.Length ? endTimes[kind].Value : 0d;
    }

    public static string NameOf(int kind)
    {
        return kind > None && kind < Names.Length ? Names[kind] : "";
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        int count = 0;
        foreach (int kind in Kinds)
        {
            if (RemainingOf(kind) > 0f)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        const float lineHeight = 20f;
        const float padding = 12f;

        var area = new Rect(MetaverseUi.Width - 270f, 238f, 260f, count * lineHeight + padding);
        GUI.Box(area, GUIContent.none);

        float y = area.y + padding * 0.5f;
        foreach (int kind in Kinds)
        {
            float remaining = RemainingOf(kind);
            if (remaining <= 0f)
            {
                continue;
            }

            GUI.Label(new Rect(area.x + 8f, y, area.width - 16f, lineHeight),
                $"{NameOf(kind)}   {Mathf.CeilToInt(remaining)}초 남음", Line);
            y += lineHeight;
        }
    }

    static GUIStyle line;

    /// <summary>
    /// One line stays one line. The default label wraps, and a wrapped line is twice as tall
    /// as the box was measured for, which pushed whatever came after it off the bottom.
    /// </summary>
    static GUIStyle Line => line ??= new GUIStyle(GUI.skin.label)
    {
        wordWrap = false,
        clipping = TextClipping.Overflow,
    };
}
