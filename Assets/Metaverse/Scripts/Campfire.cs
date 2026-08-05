using UnityEngine;

/// <summary>The cooking fire: materials in, a timed buff out.</summary>
public class Campfire : InteractStation
{
    void Reset()
    {
        Title = "Campfire";
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var buffs = player.GetComponent<PlayerBuffs>();
        if (inventory == null)
        {
            return;
        }

        GUILayout.Label($"Ore {inventory.Ore.Value}   Herb {inventory.Herb.Value}   Wood {inventory.Wood.Value}");
        if (buffs != null && buffs.Active)
        {
            GUILayout.Label($"Active: {PlayerBuffs.NameOf(buffs.Kind.Value)}  {Mathf.CeilToInt(buffs.Remaining)}s (cooking again replaces it)");
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
