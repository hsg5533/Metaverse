using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One quest at a time, taken from the board. Progress is counted on the server from the
/// same events that already exist: a monster dying and a node being harvested.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerQuests : NetworkBehaviour
{
    public const int Hunt = 0;
    public const int Gather = 1;

    public static readonly (string Text, int Kind, int Detail, int Target, int Gold, int Exp)[] Board =
    {
        ("몬스터 5마리 처치", Hunt, 0, 5, 40, 30),
        ("광석 8개 채집", Gather, (int)GatherKind.Ore, 8, 35, 20),
        ("약초 8개 채집", Gather, (int)GatherKind.Herb, 8, 35, 20),
        ("나무 6개 채집", Gather, (int)GatherKind.Wood, 6, 30, 18),
        ("몬스터 12마리 처치", Hunt, 0, 12, 90, 70),
    };

    /// <summary>Index into <see cref="Board"/>, or -1 when nothing is taken.</summary>
    public NetworkVariable<int> Quest = new(-1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Progress = new(0, writePerm: NetworkVariableWritePermission.Server);

    public bool HasQuest => Quest.Value >= 0 && Quest.Value < Board.Length;
    public bool Complete => HasQuest && Progress.Value >= Board[Quest.Value].Target;

    PlayerStats stats;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
    }

    [Rpc(SendTo.Server)]
    public void AcceptRpc(int index, RpcParams rpcParams = default)
    {
        if (!this.IsFromOwner(rpcParams) || index < 0 || index >= Board.Length)
        {
            return;
        }

        if (HasQuest)
        {
            NoticeRpc(NetText.Trim512("이미 받은 의뢰를 먼저 끝내세요."));
            return;
        }

        Quest.Value = index;
        Progress.Value = 0;
        NoticeRpc(NetText.Trim512($"의뢰 수락: {Board[index].Text}"));
    }

    [Rpc(SendTo.Server)]
    public void ClaimRpc(RpcParams rpcParams = default)
    {
        if (!this.IsFromOwner(rpcParams) || !Complete)
        {
            return;
        }

        var quest = Board[Quest.Value];
        Quest.Value = -1;
        Progress.Value = 0;
        stats.Gold.Value += quest.Gold;
        stats.GainReward(quest.Exp, 0);
        NoticeRpc(NetText.Trim512($"의뢰 완료: 골드 +{quest.Gold}"));
    }

    [Rpc(SendTo.Server)]
    public void AbandonRpc(RpcParams rpcParams = default)
    {
        if (!this.IsFromOwner(rpcParams))
        {
            return;
        }

        Quest.Value = -1;
        Progress.Value = 0;
    }

    /// <summary>Server side: called when this player lands a killing blow.</summary>
    public void OnMonsterKilled()
    {
        Advance(Hunt, 0, 1);
    }

    /// <summary>Server side: called when this player harvests a node.</summary>
    public void OnGathered(GatherKind kind, int amount)
    {
        Advance(Gather, (int)kind, amount);
    }


    void Advance(int kind, int detail, int amount)
    {
        if (!IsServer || !HasQuest || Complete)
        {
            return;
        }

        var quest = Board[Quest.Value];
        if (quest.Kind != kind || quest.Detail != detail)
        {
            return;
        }

        Progress.Value = Mathf.Min(quest.Target, Progress.Value + amount);
        if (Progress.Value >= quest.Target)
        {
            NoticeRpc(NetText.Trim512("의뢰 조건 달성. 게시판으로 돌아가세요."));
        }
    }

    /// <summary>Server side: used by the save file.</summary>
    public void Restore(int quest, int progress)
    {
        if (!IsServer)
        {
            return;
        }

        Quest.Value = quest >= 0 && quest < Board.Length ? quest : -1;
        Progress.Value = Mathf.Max(0, progress);
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Notice(text.ToString());
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

        if (!IsOwner || !IsSpawned || !HasQuest)
        {
            return;
        }

        var quest = Board[Quest.Value];
        GUILayout.BeginArea(new Rect(MetaverseUi.Width - 250, 270, 240, 28), GUI.skin.box);
        GUILayout.Label($"{quest.Text}  {Progress.Value}/{quest.Target}");
        GUILayout.EndArea();
    }
}
