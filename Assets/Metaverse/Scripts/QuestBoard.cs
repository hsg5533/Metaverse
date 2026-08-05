using UnityEngine;

/// <summary>The board on the plaza: take one job at a time, come back to claim it.</summary>
public class QuestBoard : InteractStation
{
    void Reset()
    {
        Title = "의뢰 게시판";
        PanelSize = new Vector2(360f, 240f);
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var quests = player.GetComponent<PlayerQuests>();
        if (quests == null)
        {
            return;
        }

        if (quests.HasQuest)
        {
            var current = PlayerQuests.Board[quests.Quest.Value];
            GUILayout.Label($"{current.Text}   {quests.Progress.Value}/{current.Target}");
            GUILayout.Label($"보상: 골드 {current.Gold}, 경험치 {current.Exp}");
            GUILayout.Space(6);

            if (quests.Complete)
            {
                if (GUILayout.Button("보상 받기"))
                {
                    quests.ClaimRpc();
                }
            }
            else if (GUILayout.Button("포기"))
            {
                quests.AbandonRpc();
            }

            return;
        }

        GUILayout.Label("의뢰를 고르세요:");
        for (int i = 0; i < PlayerQuests.Board.Length; i++)
        {
            var quest = PlayerQuests.Board[i];
            if (GUILayout.Button($"{quest.Text}   (골드 {quest.Gold}, 경험치 {quest.Exp})"))
            {
                quests.AcceptRpc(i);
            }
        }
    }
}
