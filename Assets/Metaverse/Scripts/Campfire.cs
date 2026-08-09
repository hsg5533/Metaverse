using UnityEngine;

/// <summary>The cooking fire: materials in, a dish out - stored in the bag until eaten.</summary>
public class Campfire : InteractStation
{
    void Awake()
    {
        // One icon row per recipe plus the resource line and up to four buff lines need more
        // room than the text-only stations: tall enough that nothing clips against the box
        // edge even with every buff running at once.
        PanelSize = new Vector2(380f, 140f + PlayerInventory.CookRecipes.Length * (MetaverseUi.ItemRowHeight + 4f));
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var gear = player.GetComponent<PlayerGear>();
        var buffs = player.GetComponent<PlayerBuffs>();
        if (inventory == null)
        {
            return;
        }

        string fish = gear != null ? $"   붕어 {gear.CountInBag(PlayerGear.FirstFish)}" : "";
        GUILayout.Label($"광석 {inventory.Ore.Value}   약초 {inventory.Herb.Value}   나무 {inventory.Wood.Value}{fish}");
        if (buffs != null && buffs.Active)
        {
            // One short line per kind instead of one long joined line, so nothing has to wrap.
            foreach (int kind in PlayerBuffs.Kinds)
            {
                DrawBuffLine(buffs, kind);
            }
        }
        GUILayout.Space(4);

        for (int i = 0; i < PlayerInventory.CookRecipes.Length; i++)
        {
            var recipe = PlayerInventory.CookRecipes[i];
            int piece = PlayerGear.FoodFirst + i;
            PlayerGear.Piece food = PlayerGear.Pieces[piece];

            var slot = GUILayoutUtility.GetRect(PanelSize.x - 24f, MetaverseUi.ItemRowHeight + 4f);
            string effect = $"{PlayerBuffs.NameOf(food.Buff)}  {Mathf.RoundToInt(food.BuffSeconds / 60f)}분";
            string cost = $"만들기  ({Cost(recipe.Ore, recipe.Herb, recipe.Wood, recipe.Fish)})";

            int index = i;
            MetaverseUi.ItemRow(slot, GearPreview.Piece + piece, food.Name, effect, cost, () => inventory.CookRpc(index));
        }

        GUILayout.Space(2);
        GUILayout.Label("가방에서 눌러 먹는다.");
    }

    static void DrawBuffLine(PlayerBuffs buffs, int kind)
    {
        float remaining = buffs.RemainingOf(kind);
        if (remaining > 0f)
        {
            GUILayout.Label($"적용 중: {PlayerBuffs.NameOf(kind)}  {Mathf.CeilToInt(remaining)}초");
        }
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
