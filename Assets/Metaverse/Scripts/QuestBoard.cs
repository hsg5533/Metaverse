using UnityEngine;

/// <summary>The board on the plaza: take one job at a time, come back to claim it.</summary>
public class QuestBoard : InteractStation
{
    void Reset()
    {
        Title = "Quest Board";
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
            GUILayout.Label($"Reward: {current.Gold} G, {current.Exp} EXP");
            GUILayout.Space(6);

            if (quests.Complete)
            {
                if (GUILayout.Button("Claim reward"))
                {
                    quests.ClaimRpc();
                }
            }
            else if (GUILayout.Button("Give up"))
            {
                quests.AbandonRpc();
            }

            return;
        }

        GUILayout.Label("Pick a job:");
        for (int i = 0; i < PlayerQuests.Board.Length; i++)
        {
            var quest = PlayerQuests.Board[i];
            if (GUILayout.Button($"{quest.Text}   ({quest.Gold} G, {quest.Exp} EXP)"))
            {
                quests.AcceptRpc(i);
            }
        }
    }
}
