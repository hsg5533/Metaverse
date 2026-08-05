using UnityEngine;

/// <summary>The cooking fire: materials in, a timed buff out.</summary>
public class Campfire : InteractStation
{
    void Reset()
    {
        Title = "모닥불";
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var buffs = player.GetComponent<PlayerBuffs>();
        if (inventory == null)
        {
            return;
        }

        GUILayout.Label($"광석 {inventory.Ore.Value}   약초 {inventory.Herb.Value}   나무 {inventory.Wood.Value}");
        if (buffs != null && buffs.Active)
        {
            GUILayout.Label($"적용 중: {PlayerBuffs.NameOf(buffs.Kind.Value)}  {Mathf.CeilToInt(buffs.Remaining)}초 (새로 먹으면 교체)");
        }
        GUILayout.Space(6);

        for (int i = 0; i < PlayerInventory.CookRecipes.Length; i++)
        {
            if (GUILayout.Button(PlayerInventory.CookRecipes[i].Name))
            {
                inventory.CookRpc(i);
            }
        }
    }
}
