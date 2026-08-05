using UnityEngine;

/// <summary>The anvil: turns gathered materials into gear upgrades, no gold involved.</summary>
public class CraftStation : InteractStation
{
    void Reset()
    {
        Title = "Anvil";
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var stats = player.GetComponent<PlayerStats>();
        if (inventory == null || stats == null)
        {
            return;
        }

        GUILayout.Label($"Ore {inventory.Ore.Value}   Wood {inventory.Wood.Value}");
        GUILayout.Label($"Weapon Lv.{stats.WeaponLevel.Value}   Armor Lv.{stats.ArmorLevel.Value}");
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
