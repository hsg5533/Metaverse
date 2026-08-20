using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The village chest: anything owned but not being carried waits here instead of filling the
/// bag. What it holds belongs to the player who opened it - the server owns every list, so
/// the panel only ever asks for a move and never performs one.
/// </summary>
public class StorageChest : InteractStation
{
    /// <summary>Rows on screen per side before the list scrolls.</summary>
    const int VisibleRows = 3;

    const float ColumnWidth = 290f;

    static float RowHeight => MetaverseUi.ItemRowHeight + 4f;

    /// <summary>One line of either side: what to draw, and what moving it does.</summary>
    readonly struct Row
    {
        public readonly int Preview;
        public readonly string Name;
        public readonly string Detail;
        public readonly System.Action Move;

        public Row(int preview, string name, string detail, System.Action move)
        {
            Preview = preview;
            Name = name;
            Detail = detail;
            Move = move;
        }
    }

    Vector2 bagScroll;
    Vector2 chestScroll;

    void Awake()
    {
        Title = "창고";

        // Both columns plus the heading, the close button below them, and the margin
        // GUILayout puts around each - budget it short and the close button falls off.
        PanelSize = new Vector2(ColumnWidth * 2f + 30f, 108f + VisibleRows * RowHeight);
    }

    protected override void DrawPanel(PlayerAvatar player)
    {
        var gear = player.GetComponent<PlayerGear>();
        var inventory = player.GetComponent<PlayerInventory>();
        if (gear == null || inventory == null)
        {
            return;
        }

        GUILayout.BeginHorizontal();
        bagScroll = Column(bagScroll, "가방", "맡기기", Carried(gear, inventory));
        chestScroll = Column(chestScroll, "창고", "찾기", Kept(gear, inventory));
        GUILayout.EndHorizontal();
    }

    /// <summary>The bag, in the order it was picked up: gear one by one, materials by stack.</summary>
    static List<Row> Carried(PlayerGear gear, PlayerInventory inventory)
    {
        var rows = new List<Row>();
        for (int i = 0; i < gear.Bag.Count; i++)
        {
            int entry = gear.Bag[i];
            if (entry >= 0)
            {
                int index = i;
                rows.Add(Piece(entry, () => gear.MoveStoredRpc(index, true)));
                continue;
            }

            int material = PlayerGear.MaterialOf(entry);
            rows.Add(
                Stack(
                    material,
                    inventory.CountOf(material),
                    () => inventory.MoveMaterialRpc(material, true)
                )
            );
        }

        return rows;
    }

    /// <summary>The same for the chest side: its gear, then whatever stacks it is holding.</summary>
    static List<Row> Kept(PlayerGear gear, PlayerInventory inventory)
    {
        var rows = new List<Row>();
        for (int i = 0; i < gear.Storage.Count; i++)
        {
            int index = i;
            rows.Add(Piece(gear.Storage[i], () => gear.MoveStoredRpc(index, false)));
        }

        // The list arrives from the server, so on a client that has not had it yet there is
        // nothing to walk - which is not the same as three zeroes.
        for (int material = 0; material < inventory.Stored.Count; material++)
        {
            if (inventory.Stored[material] > 0)
            {
                int slot = material;
                rows.Add(
                    Stack(
                        slot,
                        inventory.Stored[slot],
                        () => inventory.MoveMaterialRpc(slot, false)
                    )
                );
            }
        }

        return rows;
    }

    static Row Piece(int piece, System.Action move)
    {
        return new Row(
            GearPreview.Piece + piece,
            PlayerGear.Pieces[piece].Name,
            PlayerGear.DetailOf(piece),
            move
        );
    }

    static Row Stack(int material, int count, System.Action move)
    {
        return new Row(
            GearPreview.Ore + material,
            PlayerInventory.Slots[material],
            $"{count}개",
            move
        );
    }

    static Vector2 Column(Vector2 scroll, string label, string action, List<Row> rows)
    {
        GUILayout.BeginVertical(GUILayout.Width(ColumnWidth));
        GUILayout.Label($"<b>{label}</b>", MetaverseUi.Rich);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(VisibleRows * RowHeight));
        foreach (var row in rows)
        {
            // Room for the scroll bar, which the rows would otherwise run underneath.
            var slot = GUILayoutUtility.GetRect(ColumnWidth - 28f, RowHeight);
            MetaverseUi.ItemRow(slot, row.Preview, row.Name, row.Detail, action, row.Move);
        }
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
        return scroll;
    }
}
