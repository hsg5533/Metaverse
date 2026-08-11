using UnityEngine;

/// <summary>The cooking fire: materials in, a dish out - stored in the bag until eaten.</summary>
public class Campfire : InteractStation
{
    /// <summary>
    /// How many recipes are on screen at once. The list is longer than that now, and a panel
    /// tall enough to hold all of it would be taller than the screen it is drawn on a phone in.
    /// </summary>
    const int VisibleRecipes = 3;

    static float RowHeight => MetaverseUi.ItemRowHeight + 4f;

    Vector2 scroll;

    void Awake()
    {
        // Everything that is not the scrolling list: the title, the line of materials, and
        // the close button the station draws below all of it - plus the margin GUILayout puts
        // around each of them. Budget this short and the close button is the thing that falls
        // off the bottom, which on a phone leaves no way out of the panel.
        PanelSize = new Vector2(380f, 108f + VisibleRecipes * RowHeight);
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var gear = player.GetComponent<PlayerGear>();
        if (inventory == null)
        {
            return;
        }

        string fish = gear != null ? $"   붕어 {gear.CountInBag(PlayerGear.FirstFish)}" : "";
        GUILayout.Label(
            $"광석 {inventory.Ore.Value}   약초 {inventory.Herb.Value}   나무 {inventory.Wood.Value}{fish}"
        );
        GUILayout.Space(4);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(VisibleRecipes * RowHeight));

        for (int i = 0; i < PlayerInventory.CookRecipes.Length; i++)
        {
            var recipe = PlayerInventory.CookRecipes[i];
            int piece = PlayerGear.FoodFirst + i;
            PlayerGear.Piece food = PlayerGear.Pieces[piece];

            // Room for the scroll bar, which the rows would otherwise run underneath.
            var slot = GUILayoutUtility.GetRect(PanelSize.x - 44f, RowHeight);
            string effect =
                food.Heal > 0
                    ? $"체력 {food.Heal}% 회복"
                    : $"{PlayerBuffs.NameOf(food.Buff)}  {Mathf.RoundToInt(food.BuffSeconds / 60f)}분";
            string cost = $"만들기  ({Cost(recipe.Ore, recipe.Herb, recipe.Wood, recipe.Fish)})";

            int index = i;
            MetaverseUi.ItemRow(
                slot,
                GearPreview.Piece + piece,
                food.Name,
                effect,
                cost,
                () => inventory.CookRpc(index)
            );
        }

        GUILayout.EndScrollView();
    }

    static string Cost(int ore, int herb, int wood, int fish)
    {
        string text = fish >= 0 ? $"{PlayerGear.Pieces[fish].Name}1 " : "";
        if (ore > 0)
        {
            text += $"광석{ore} ";
        }

        if (herb > 0)
        {
            text += $"약초{herb} ";
        }

        if (wood > 0)
        {
            text += $"나무{wood}";
        }

        return text.Trim();
    }
}
