using UnityEngine;

/// <summary>The anvil: turns gathered materials into gear upgrades, no gold involved.</summary>
public class CraftStation : InteractStation
{
    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var stats = player.GetComponent<PlayerStats>();
        if (inventory == null || stats == null)
        {
            return;
        }

        GUILayout.Label($"광석 {inventory.Ore.Value}   나무 {inventory.Wood.Value}");
        GUILayout.Label($"검 Lv.{stats.WeaponLevel.Value}   방어구 Lv.{stats.ArmorLevel.Value}");
        GUILayout.Space(6);

        for (int i = 0; i < PlayerInventory.CraftRecipes.Length; i++)
        {
            if (GUILayout.Button(PlayerInventory.CraftRecipes[i].Name))
            {
                inventory.CraftRpc(i);
            }
        }
    }
}
