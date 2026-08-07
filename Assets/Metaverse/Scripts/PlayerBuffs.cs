using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One timed buff at a time, from cooking. The server sets the kind and the moment it ends;
/// clients compare that against the synchronised server clock, so no ticking is replicated.
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

    public NetworkVariable<int> Kind = new(None, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<double> EndTime = new(0d, writePerm: NetworkVariableWritePermission.Server);

    public bool Active => Kind.Value != None && Remaining > 0f;

    public float Remaining
    {
        get
        {
            if (!IsSpawned || NetworkManager == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(EndTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    public int AttackBonus => Active && Kind.Value == Attack ? AttackAmount : 0;
    public int DefenseBonus => Active && Kind.Value == Defense ? DefenseAmount : 0;
    public float SpeedMultiplier => Active && Kind.Value == Speed ? SpeedAmount : 1f;

    /// <summary>Server side: replaces whatever was running.</summary>
    public void Apply(int kind, float seconds)
    {
        if (!IsServer)
        {
            return;
        }

        Kind.Value = kind;
        EndTime.Value = NetworkManager.ServerTime.Time + seconds;
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

        if (!IsOwner || !IsSpawned || !Active)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(MetaverseUi.Width - 250, 238, 240, 28), GUI.skin.box);
        GUILayout.Label($"{NameOf(Kind.Value)}   {Mathf.CeilToInt(Remaining)}초 남음");
        GUILayout.EndArea();
    }
}
