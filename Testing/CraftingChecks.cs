using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CraftingChecks
{
    private static ItemId Planks => Items.FromBlock(BlockType.WoodenPlanks);
    private static ItemId Table => Items.FromBlock(BlockType.CraftingTable);
    private static ItemId Dirt => Items.FromBlock(BlockType.Dirt);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static int Run()
    {
        try { Recipes(); Transactions(); ClosingAndDeath(); BlocksAndPersistence(); Layout(); Console.WriteLine("PASS: All crafting checks."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static CraftingGrid Fill(int width, int count = 1, int x = 0, int y = 0)
    {
        var grid = new CraftingGrid(width);
        for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++) grid.SetSlot((row + y) * width + col + x, new ItemStack(Planks, count));
        return grid;
    }
    public static void Recipes()
    {
        Require(CraftingRecipes.All.Length == 29, "Survival recipe registry is incomplete.");
        foreach (int width in new[] { 2, 3 }) for (int y = 0; y <= width - 2; y++) for (int x = 0; x <= width - 2; x++)
        {
            var grid = Fill(width, 3, x, y);
            for (int i = 0; i < 100; i++) Require(grid.Preview == new ItemStack(Table, 1), "Valid offset failed or preview changed ingredients.");
            Require(Enumerable.Range(0, grid.SlotCount).Sum(i => grid[i].Count) == 12, "Preview manufactured/consumed items.");
            int first = y * width + x;
            grid.SetSlot(first, default); Require(grid.Preview.IsEmpty, "Missing ingredient matched.");
            grid.SetSlot(first, new ItemStack(Dirt, 3)); Require(grid.Preview.IsEmpty, "Wrong ingredient matched.");
            grid.SetSlot(first, new ItemStack(Planks, 3));
            if (width == 3) { int empty = Enumerable.Range(0, 9).First(i => grid[i].IsEmpty); grid.SetSlot(empty, new ItemStack(Planks, 1)); Require(grid.Preview.IsEmpty, "Extra ingredients were ignored."); }
        }
        var scattered = new CraftingGrid(3);
        foreach (int i in new[] { 0, 2, 6, 8 }) scattered.SetSlot(i, new ItemStack(Planks, 1));
        Require(scattered.Preview.IsEmpty, "Arbitrary rearrangement matched shaped recipe.");
        var asymmetric = new ShapedRecipe(2, 2, new[] { new CraftingIngredient(Planks, 2), new CraftingIngredient(Dirt), default, new CraftingIngredient(Table) }, new ItemStack(Table, 2));
        var pattern = new CraftingGrid(2); pattern.SetSlot(0, new ItemStack(Planks, 1)); pattern.SetSlot(1, new ItemStack(Dirt, 1)); pattern.SetSlot(3, new ItemStack(Table, 1));
        Require(pattern.PreviewFor(new[] { asymmetric }).IsEmpty, "Required ingredient quantity was ignored.");
        pattern.SetSlot(0, new ItemStack(Planks, 2)); Require(pattern.PreviewFor(new[] { asymmetric }).Count == 2, "Extensible quantity recipe failed.");
        pattern.SetSlot(0, new ItemStack(Dirt, 1)); pattern.SetSlot(1, new ItemStack(Planks, 2)); pattern.SetSlot(2, new ItemStack(Table, 1)); pattern.SetSlot(3, default);
        Require(pattern.PreviewFor(new[] { asymmetric }).IsEmpty, "Implicit mirror was accepted.");
    }
    public static void Transactions()
    {
        var inventory = new Inventory(); var mouse = new InventoryInteraction(inventory); var grid = Fill(2, 3);
        Require(grid.TryCraft(mouse) && mouse.Cursor == new ItemStack(Table, 1) && grid[0].Count == 2, "Craft did not consume once.");
        Require(grid.TryCraft(mouse) && mouse.Cursor.Count == 2 && grid.Preview.Count == 1, "Repeat crafting failed.");
        Require(grid.TryCraft(mouse) && mouse.Cursor.Count == 3 && grid.Preview.IsEmpty && !grid.TryCraft(mouse), "Exhaustion duplicated result.");
        mouse.Click(0, false); inventory.SetSlot(1, new ItemStack(Dirt, 5)); mouse.Click(1, false); grid = Fill(2);
        Require(!grid.TryCraft(mouse) && grid[0].Count == 1 && mouse.Cursor.Count == 5, "Incompatible cursor consumed ingredients.");
        mouse.Click(1, false); inventory.SetSlot(2, new ItemStack(Table, 64)); mouse.Click(2, false);
        Require(!grid.TryCraft(mouse) && grid[0].Count == 1 && mouse.Cursor.Count == 64, "Full cursor accepted result.");
        mouse.Click(2, true); Require(grid.TryCraft(mouse) && mouse.Cursor.Count == 64, "Compatible 63-stack did not accept one.");
        inventory = new Inventory(); mouse = new InventoryInteraction(inventory);
        for (int i = 0; i < 36; i++) inventory.SetSlot(i, new ItemStack(Dirt, 64));
        grid = Fill(3, 2, 1, 1); Require(grid.TryCraft(mouse) && mouse.Cursor.Count == 1, "Full inventory incorrectly blocks an empty cursor.");
        var quantityRecipe = new ShapedRecipe(1, 1, new[] {new CraftingIngredient(Planks,2)},new ItemStack(Table,4));
        inventory = new Inventory(); mouse = new InventoryInteraction(inventory); grid = new CraftingGrid(2); grid.SetSlot(0,new ItemStack(Planks,6));
        inventory.SetSlot(0,new ItemStack(Table,62)); mouse.Click(0,false);
        Require(!grid.TryCraft(mouse,new[]{quantityRecipe}) && grid[0].Count==6 && mouse.Cursor.Count==62,"Partial multi-item output consumed inputs.");
        mouse.Click(0,false);
        for(int i=0;i<3;i++)Require(grid.TryCraft(mouse,new[]{quantityRecipe}),"Quantity recipe stopped early.");
        Require(mouse.Cursor.Count==12 && grid[0].IsEmpty && grid.PreviewFor(new[]{quantityRecipe}).IsEmpty,"Recipe quantities were not consumed/produced exactly.");
        var random = new Random(110); inventory = new Inventory(); mouse = new InventoryInteraction(inventory); grid = new CraftingGrid(3);
        inventory.SetSlot(0, new ItemStack(Planks, 64)); inventory.SetSlot(1, new ItemStack(Table, 3));
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            switch (random.Next(3))
            {
                case 0: mouse.Click(random.Next(36), random.Next(2) == 0); break;
                case 1: grid.Click(random.Next(9), mouse, random.Next(2) == 0); break;
                case 2: grid.TryCraft(mouse); break;
            }
            int value = 0;
            for (int i = 0; i < 36; i++) value += Value(inventory[i]);
            for (int i = 0; i < 9; i++) value += Value(grid[i]);
            value += Value(mouse.Cursor);
            Require(value == 152, "Mixed inventory/crafting operations lost/duplicated ingredients or output.");
        }
        static int Value(ItemStack stack) => stack.IsEmpty ? 0 : stack.Count * (stack.Item == Table ? 8 : stack.Item==Items.FromBlock(BlockType.Chest)?16
            : stack.Item==Planks?2:stack.Item==Items.Stick?1:Items.Get(stack.Item).Tool switch{ToolKind.Pickaxe or ToolKind.Axe=>8,ToolKind.Sword=>5,ToolKind.Shovel=>4,_=>0});
    }
    private static GameSession Session(World world = null) => new("crafting", new WorldSave(world ?? PigChecks.Flat(), "Crafting", new Vector3(40.5f, 1, 40.5f), 0, 0, new Inventory()));
    public static void ClosingAndDeath()
    {
        var session = Session(); session.Crafting.SetSlot(0, new ItemStack(Planks, 27)); session.Crafting.Click(0, session.InventoryInteraction, true);
        Require(session.InventoryInteraction.Cursor.Count == 13 && session.Crafting[0].Count == 14, "Crafting half split changed rules.");
        Require(session.TryReturnInterfaceItems() && session.Inventory.Selected.Count == 27 && session.Items.Entities.Count == 0, "Closing lost ingredients/cursor.");
        for (int i = 0; i < 36; i++) session.Inventory.SetSlot(i, new ItemStack(Dirt, 64));
        session.Crafting.SetSlot(1, new ItemStack(Planks, 17));
        Require(session.TryReturnInterfaceItems() && session.Items.Entities.Single().Count == 17 && session.Crafting[1].IsEmpty, "Overflow ingredients did not drop atomically.");
        Require(session.TryReturnInterfaceItems() && session.Items.Entities.Count == 1, "Repeated close duplicated overflow.");
        session = Session(); for(int i=0;i<4;i++)session.Crafting.SetSlot(i,new ItemStack(Planks,5)); session.Crafting.Click(0, session.InventoryInteraction, true);
        Require(session.Crafting.Preview.Item==Table,"Death fixture needs a valid preview.");
        session.Player.ApplyDamage(20); Require(session.HandleDeath() && !session.HandleDeath(), "Death was not idempotent.");
        Require(session.Items.Entities.Sum(i => i.Count) == 20 && session.Items.Entities.All(i=>i.Item==Planks)
            && session.Crafting.Preview.IsEmpty && session.InventoryInteraction.Cursor.IsEmpty, "Death lost/duplicated crafting inputs, preview or cursor.");
        session.Respawn(); Require(session.Inventory.Selected.IsEmpty && session.Player.Health == 20 && session.Items.Entities.Sum(i => i.Count) == 20, "Respawn granted supplies or removed drops.");
        session = Session(); for (int i = 0; i < 36; i++) session.Inventory.SetSlot(i, new ItemStack(Dirt, 64));
        for (int y = 0; y < 6; y++) for (int z = 37; z < 45; z++) for (int x = 37; x < 45; x++) session.World.SetGeneratedBlock(x, y, z, BlockType.Stone);
        session.Crafting.SetSlot(0, new ItemStack(Planks, 9));
        Require(!session.TryReturnInterfaceItems() && session.Crafting[0].Count == 9, "Failed overflow placement discarded inputs.");
        try { session.Capture(); throw new Exception("Blocked save discarded inputs."); } catch (InvalidOperationException) { }
        session.Inventory.SetSlot(5, default); Require(session.TryReturnInterfaceItems() && session.Inventory[5].Count == 9, "Blocked interface could not recover after making room.");
    }
    public static void BlocksAndPersistence()
    {
        Require((byte)BlockType.CraftingTable == 24 && Table.Value == 24 && Blocks.IsSolid(BlockType.CraftingTable) && Blocks.IsOpaque(BlockType.CraftingTable), "Table IDs/properties wrong.");
        Require(Blocks.Tile(BlockType.CraftingTable, Vector3.Up) == 16 && Blocks.Tile(BlockType.CraftingTable, Vector3.Right) == 17
            && Blocks.Tile(BlockType.CraftingTable, Vector3.Down) == 7, "Table face mappings incorrect.");
        foreach (WorldSize size in Enum.GetValues<WorldSize>())
        {
            var world = new World(173, size, false); var session = Session(world);
            Require(Enumerable.Range(0, 36).All(i => Inventory.CreateNewWorld()[i].IsEmpty && session.Inventory[i].IsEmpty), "New world granted items.");
            session.Inventory.SetSlot(0, new ItemStack(Table, 1)); var cell = new Point3(15, 4, 16);
            Require(BlockPlacement.TryPlace(world, session.Inventory, cell, session.Player.Bounds) && session.Inventory[0].IsEmpty, "Table placement did not consume item.");
            Require(session.TryOpenCraftingTable(new BlockHit(cell, default, 5)) && session.Crafting.Width == 3
                && !session.TryOpenCraftingTable(new BlockHit(cell, default, 7)), "Table reach/interaction invalid.");
            session.Crafting.SetSlot(0, new ItemStack(Planks, 37)); session.Crafting.Click(0, session.InventoryInteraction, true);
            WorldSave saved = RoundTrip(session.Capture());
            Require(saved.World.GetBlock(15, 4, 16) == BlockType.CraftingTable && Enumerable.Range(0,36).Sum(i=>saved.Inventory[i].Count) == 37, "Saving lost table/inputs/cursor.");
            var restored = new GameSession("restored", saved);
            Require(restored.Inventory[0] == new ItemStack(Planks, 37) && restored.Crafting.Preview.IsEmpty, "Saved inventory altered or grid duplicated on load.");
            session.Mining.Update(world, session.Items, new BlockHit(cell, new Point3(0,1,0), 4), true, Blocks.Get(BlockType.CraftingTable).MiningSeconds);
            Require(world.GetBlock(15,4,16) == BlockType.Air && session.Items.Entities.Single().Item == Table, "Table mining/drop failed.");
            var mesh = new ChunkMeshBuilder(); mesh.AppendItem(BlockType.CraftingTable, Vector3.Zero);
            Require(mesh.VertexCount == 36, "Table is not a full cube.");
            world = null; session = null; restored = null; saved = null;
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
    }
    private static WorldSave RoundTrip(WorldSave save)
    {
        string path = Path.Combine(AppContext.BaseDirectory, ".crafting-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            WorldSerializer.Write(stream, save); stream.Position = 0;
            return WorldSerializer.Read(stream);
        }
        finally { File.Delete(path); }
    }
    public static void Layout()
    {
        foreach (var viewport in new[] {new Viewport(0,0,320,240),new Viewport(0,0,801,603),new Viewport(0,0,1280,720),new Viewport(0,0,1920,1080)})
        foreach (int width in new[] { 2,3 })
        {
            Rectangle panel=InventoryLayout.Panel(viewport); Require(viewport.Bounds.Contains(panel), "Crafting panel leaves viewport.");
            var slots=Enumerable.Range(0,36).Select(i=>InventoryLayout.Slot(viewport,i)).Concat(Enumerable.Range(0,width*width).Select(i=>InventoryLayout.CraftingSlot(viewport,width,i)))
                .Append(InventoryLayout.Output(viewport,width)).ToArray();
            for(int i=0;i<slots.Length;i++) { Require(panel.Contains(slots[i]),"Slot leaves panel."); for(int j=0;j<i;j++)Require(!slots[i].Intersects(slots[j]),"Slots overlap."); }
            Require(slots.All(s=>!s.Intersects(InventoryLayout.Arrow(viewport,width))),"Arrow overlaps a slot.");
        }
    }
}
