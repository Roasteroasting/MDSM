using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class IndustrialChecks
{
    internal static readonly (string Name, Action Check)[] All =
    {
        ("Industrial progression, kiln and station transactions", Progression),
        ("All dyes, stained glass meshes, connections and drops", DyesAndGlass),
        ("Infinite copper, clay and flower patches with compressed chunk round trips", Generation)
    };

    internal static int Run()
    {
        foreach (var test in All)
        {
            try { test.Check(); Console.WriteLine("PASS: " + test.Name); }
            catch (Exception e) { Console.Error.WriteLine("FAIL: " + test.Name + ": " + e); return 1; }
        }
        return 0;
    }

    private static ItemId B(BlockType block) => Items.FromBlock(block);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static CraftingGrid Grid(CraftingStation station, int width, params ItemId[] inputs)
    {
        var grid = new CraftingGrid(width, station);
        for (int i = 0; i < inputs.Length; i++) if (inputs[i] != default) grid.SetSlot(i, new(inputs[i], 1));
        return grid;
    }

    private static void Craft(CraftingGrid grid, ItemId result, int count = 1)
    {
        var mouse = new InventoryInteraction(new Inventory());
        Check(grid.Preview == new ItemStack(result, count) && grid.TryCraft(mouse) && mouse.Cursor == new ItemStack(result, count)
            && grid.Preview.IsEmpty, "Recipe failed to craft and consume its inputs for " + Items.Name(result));
    }

    private static void Progression()
    {
        Span<ItemId> drops = stackalloc ItemId[5];
        Check(MiningRules.Drops(BlockType.Clay, default, new Random(173), drops) == 4
            && !drops[..4].ContainsAnyExcept(Items.ClayBall), "Clay did not release four clay balls.");
        var stonePick = new ItemStack(Items.Tool(ToolTier.Stone, ToolKind.Pickaxe), 1);
        Check(MiningRules.Drop(BlockType.CopperOre, stonePick, null) == B(BlockType.CopperOre)
            && MiningRules.Drop(BlockType.CopperOre, default, null) == null, "Copper ore harvest requirement changed.");
        ItemId ore = B(BlockType.CopperOre), brickBlock = B(BlockType.BrickBlock), bench = B(BlockType.MetalworkingBench);
        ItemId kilnItem = B(BlockType.BrickKiln), planks = B(BlockType.WoodenPlanks), iron = Items.IronIngot;
        Check(Smelting.Result(ore, BlockType.Furnace) == Items.CopperIngot && Smelting.Result(ore, BlockType.BlastFurnace) == Items.CopperIngot
            && Smelting.Result(ore, BlockType.BrickKiln) == default && Smelting.Result(Items.ClayBall, BlockType.Furnace) == Items.Brick,
            "Furnace progression or kiln input restrictions failed.");
        Craft(Grid(CraftingStation.Standard, 2, Items.Brick, Items.Brick, Items.Brick, Items.Brick), brickBlock);
        Craft(Grid(CraftingStation.Standard, 3, brickBlock, brickBlock, brickBlock, brickBlock, default, brickBlock,
            brickBlock, brickBlock, brickBlock), kilnItem);
        Craft(Grid(CraftingStation.Standard, 3, iron, B(BlockType.CraftingTable), iron, planks, planks, planks), bench);
        Craft(Grid(CraftingStation.Standard, 3, Items.Brick, default, Items.Brick, default, Items.Brick), Items.MortarAndPestle);
        Craft(Grid(CraftingStation.Standard, 3, Items.ClayBall, default, Items.ClayBall, default, Items.ClayBall), Items.UnfiredPot);
        Craft(Grid(CraftingStation.Standard, 3, Enumerable.Repeat(Items.CopperIngot, 9).ToArray()), B(BlockType.CopperBlock));
        Craft(Grid(CraftingStation.Standard, 2, B(BlockType.CopperBlock)), Items.CopperIngot, 9);
        var wire = Enumerable.Repeat(Items.CopperIngot, 3).ToArray();
        Check(Grid(CraftingStation.Standard, 3, wire).Preview.IsEmpty, "Wire could be made away from the metalworking bench.");
        var world = PigChecks.Flat();
        var cell = new Point3(19, 1, 20);
        world.SetBlock(cell.X, cell.Y, cell.Z, BlockType.MetalworkingBench);
        var session = new GameSession("industrial", new WorldSave(world, "Industrial", new(20, 1, 20), 0, 0, new Inventory()));
        Check(!session.TryOpenCraftingTable(new(cell, default, BlockRaycaster.Reach + 1))
            && session.TryOpenCraftingTable(new(cell, default, 2)) && session.Crafting.Station == CraftingStation.Metalworking,
            "Placed bench ignored reach or opened the wrong crafting station.");
        Craft(Grid(session.Crafting.Station, 3, wire), Items.CopperWire, 6);
        Check(Grid(session.Crafting.Station, 3, iron, Items.CopperWire, iron).Preview.IsEmpty,
            "The retired bench recipe is still craftable.");
        CheckRetiredParts();

        var kiln = new BlockContainer(cell, BlockType.BrickKiln, null);
        kiln.SetSlot(0, new(Items.UnfiredPot, 2)); kiln.SetSlot(1, new(Items.Charcoal, 1));
        kiln.Update(3);
        byte[] frozen = StreamingJson.Encode(kiln.Capture());
        var restored = new BlockContainer(cell, BlockType.BrickKiln, null);
        restored.Restore(StreamingJson.Decode<BlockEntityState>(frozen)); restored.Update(2);
        Check(restored[2] == new ItemStack(Items.CeramicPot, 1) && restored[0].Count == 1
            && restored.Burn == 70 && Smelting.Result(Items.UnfiredPot, BlockType.Furnace) == default,
            "Clay firing or persisted kiln progress failed.");
    }

    private static void CheckRetiredParts()
    {
        var retired = new ItemId(340);
        Check(!Items.IsDefined(retired) && !CreativeCatalog.All.Contains(retired) && !Items.Sprites.Contains(retired)
            && !Items.Sprites.ToArray().Any(item => Items.Get(item).Sprite == 86),
            "The retired item or its atlas assignment still exists.");
        var oldStack = new ItemStack(Items.IronIngot, 15);
        var manifest = new InfiniteManifest {Inventory = new[] {oldStack}};
        var chunk = new ChunkGameplay
        {
            Containers = new[] {new BlockEntityState(default, BlockType.Chest, new[] {oldStack})},
            Drops = new[] {new LocatedDrop(new DroppedItemState(Items.IronIngot, 15, default, 0, 0), default)}
        };
        byte[] OldJson<T>(T value) => Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(StreamingJson.Encode(value)).Replace("259", "340", StringComparison.Ordinal));
        var migratedChunk = StreamingJson.Decode<ChunkGameplay>(OldJson(chunk));
        Check(StreamingJson.Decode<InfiniteManifest>(OldJson(manifest)).Inventory.Single() == oldStack
            && migratedChunk.Containers.Single().Slots.Single() == oldStack
            && migratedChunk.Drops.Single().State.Stack == oldStack,
            "Infinite saves did not recover the iron from retired parts.");
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        WorldSerializer.WriteStack(writer, oldStack);
        data.Position = 0;
        writer.Write(retired.Value);
        data.Position = 0;
        using var reader = new BinaryReader(data);
        Check(WorldSerializer.ReadStack(reader) == oldStack, "Binary saves did not recover the iron from retired parts.");
    }

    private static void DyesAndGlass()
    {
        var dyes = new HashSet<ItemId>();
        foreach (var recipe in CraftingRecipes.All)
            if (recipe.Station == CraftingStation.Mortar) dyes.Add(recipe.Output.Item);
        Check(dyes.SetEquals(Enum.GetValues<DyeColor>().Select(Items.Dye)) && Items.StackLimit(Items.MortarAndPestle) == 1,
            "Mortar palette or reuse metadata is incomplete.");
        ItemId redFlower = B(BlockType.RedFlower);
        Check(Grid(CraftingStation.Standard, 2, redFlower).Preview.IsEmpty, "Dye bypassed the mortar.");
        Craft(Grid(CraftingStation.Mortar, 2, default, default, redFlower), Items.Dye(DyeColor.Red));
        Craft(Grid(CraftingStation.Mortar, 2, Items.Dye(DyeColor.White), default, default, Items.Dye(DyeColor.Blue)),
            Items.Dye(DyeColor.LightBlue), 2);
        var builder = new ChunkMeshBuilder();
        var tints = new HashSet<Microsoft.Xna.Framework.Color>();
        foreach (DyeColor color in Enum.GetValues<DyeColor>())
        {
            BlockType cube = DyePalette.Glass(color), pane = DyePalette.Glass(color, true);
            foreach (var (block, clear) in new[] { (cube, BlockType.Glass), (pane, BlockType.GlassPane) })
            {
                var recipe = Enumerable.Repeat(B(clear), 9).ToArray(); recipe[4] = Items.Dye(color);
                Craft(Grid(CraftingStation.Standard, 3, recipe), B(block), 8);
                Check(Items.Obtainable.Contains(B(block)) && Blocks.Get(block).SideTile == DyePalette.GlassTile
                    && !Blocks.IsOpaque(block) && MiningRules.Drop(block, default, null) == null,
                    "Tinted glass registration, transparency or drops differ from the clear counterpart.");
                builder.AppendInventoryItem(block);
                Check(builder.VertexCount > 0, "Stained glass had no inventory geometry.");
                if (block == cube) tints.Add(builder.Vertices[0].Color);
            }
            Craft(Grid(CraftingStation.Standard, 3, Enumerable.Repeat(B(cube), 6).ToArray()), B(pane), 16);
            Check(Blocks.ConnectsPane(pane) && Blocks.ConnectsPane(cube), "Stained glass failed pane connectivity.");
        }
        Check(tints.Count == DyePalette.Count, "The shared glass atlas lost per-color vertex tinting.");
        var world = PigChecks.Flat();
        world.SetBlock(15, 2, 16, DyePalette.Glass(DyeColor.Blue, true));
        builder.BuildBlock(world, new(15, 2, 16));
        float isolated = builder.Vertices[..builder.VertexCount].Max(v => v.Position.X);
        world.SetBlock(16, 2, 16, DyePalette.Glass(DyeColor.Orange, true));
        builder.BuildBlock(world, new(15, 2, 16));
        float connected = builder.Vertices[..builder.VertexCount].Max(v => v.Position.X);
        Check(isolated < connected && connected == 16, "Tinted pane did not reach its neighbor across a chunk edge.");
        foreach (var flower in new[] {BlockType.Cornflower, BlockType.Daisy, BlockType.Marigold})
            Check(Blocks.Flower(flower) && Blocks.Get(flower).Shape == BlockShape.Cross && !Blocks.IsSolid(flower)
                && MiningRules.Drop(flower, default, null) == B(flower), "New flower lost selection, drops, or placement support.");
    }

    private static void Generation()
    {
        int clay = 0, copper = 0, springFlowers = 0, clustered = 0;
        var species = new HashSet<BlockType>();
        var addresses = new ChunkAddress[] { new(0,0), new(8,-9), new(-24,18), new(50,35), new(-25,18), new(9,-9) };
        foreach (int seed in new[] {173, -907, 137})
        {
            var generator = new BetaDensityGenerator(seed);
            foreach (var address in addresses)
            {
                var features = new InfiniteFeatures(generator, seed, address, default, Season.Spring);
                byte[] pre = features.GenerateTerrain();
                var snapshot = features.Generate();
                BlockType At(int x, int y, int z) => (BlockType)snapshot.Blocks[Coordinates.Index(x,y,z)];
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) for (int y = 1; y < 127; y++)
                {
                    BlockType block = At(x,y,z);
                    BlockType basis = (BlockType)pre[Coordinates.Index(x,y,z)];
                    if (block == BlockType.CopperOre)
                    {
                        Check(basis == BlockType.Stone && y is >= 19 and <= 63, "Copper replaced non-stone or left its underground range.");
                        copper++;
                    }
                    if (block == BlockType.Clay)
                    {
                        Check(basis is BlockType.Sand or BlockType.Dirt or BlockType.Gravel || basis == BlockType.Stone,
                            "Clay displaced water or indestructible terrain.");
                        clay++;
                    }
                    if (!Blocks.Flower(block)) continue;
                    Check(At(x,y-1,z) == BlockType.Grass, "Flower generated without growing ground.");
                    species.Add(block);
                    if (block is BlockType.Peony or BlockType.Violet) springFlowers++;
                    else if (x>0 && Blocks.Flower(At(x-1,y,z)) || z>0 && Blocks.Flower(At(x,y,z-1))) clustered++;
                }
            }
        }
        Check(copper > 100 && clay > 0 && springFlowers > 0 && clustered > 10
            && species.Contains(BlockType.Cornflower) && species.Contains(BlockType.Daisy) && species.Contains(BlockType.Marigold),
            $"Expected natural deposits and flower patches, found copper={copper}, clay={clay}, spring={springFlowers}, adjacent={clustered}, species={string.Join(',', species)}.");
        var edge = addresses[2];
        var reordered = new BetaDensityGenerator(173);
        reordered.GenerateComplete(new(edge.X + 1, edge.Z), season: Season.Spring);
        var first = reordered.GenerateComplete(edge, season: Season.Spring);
        var second = new BetaDensityGenerator(173).GenerateComplete(edge, season: Season.Spring);
        Check(first.Blocks.SequenceEqual(second.Blocks) && first.Metadata.SequenceEqual(second.Metadata), "Industrial features depended on chunk order.");
        string root = Path.Combine(AppContext.BaseDirectory, ".industrial-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        World streamed = null;
        try
        {
            using (var store = new ChunkRegionStore(root))
                store.WriteAsync(edge, first.Encode()).GetAwaiter().GetResult();
            var kilnCell = new Point3(edge.BlockX+14,110,edge.BlockZ+3);
            var paneCell = new Point3(edge.BlockX+15,110,edge.BlockZ+3);
            BlockType pane = DyePalette.Glass(DyeColor.Brown, true);
            streamed = World.OpenStreamed(173, root, loaded:2, render:0, simulation:0);
            Pump(edge, () => streamed.IsLoaded(kilnCell));
            Check(streamed.SetBlock(kilnCell.X,kilnCell.Y,kilnCell.Z,BlockType.BrickKiln)
                && streamed.SetBlock(paneCell.X,paneCell.Y,paneCell.Z,pane), "Could not place industry blocks in a streamed chunk.");
            var kiln = streamed.Containers.Get(kilnCell);
            kiln.SetSlot(0, new(Items.UnfiredPot, 2)); kiln.SetSlot(1, new(Items.Charcoal, 1));
            streamed.Containers.Update(3);
            Pump(new(edge.X+8,edge.Z-5), () => !streamed.IsLoaded(kilnCell));
            Check(!streamed.Containers.Entries.ContainsKey(kilnCell), "Unloading retained the kiln in memory.");
            streamed.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            streamed = World.OpenStreamed(173, root, loaded:2, render:0, simulation:0);
            Pump(edge, () => streamed.IsLoaded(kilnCell));
            var loadedKiln = streamed.Containers.Get(kilnCell);
            Check(streamed.GetBlock(paneCell.X,paneCell.Y,paneCell.Z) == pane && loadedKiln.Progress == 6
                && loadedKiln[0].Count == 2 && loadedKiln.Burn == 74, "Compressed chunk lost the pane or partial kiln firing.");
            streamed.Containers.Update(2);
            Check(loadedKiln[2] == new ItemStack(Items.CeramicPot,1), "Loaded kiln did not resume exactly one firing.");
            byte[] expected = first.Blocks;
            expected[Coordinates.Index(14,110,3)] = (byte)BlockType.BrickKiln;
            expected[Coordinates.Index(15,110,3)] = (byte)pane;
            Check(streamed.GetChunk(edge.X,edge.Z).Blocks.SequenceEqual(expected),
                "Compressed region did not preserve terrain and edited block IDs exactly.");
        }
        finally
        {
            streamed?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
        Console.WriteLine($"18 Infinite chunks: {copper} copper ore, {clay} clay, {springFlowers} spring flowers, {clustered} adjacent patch flowers.");

        void Pump(ChunkAddress center, Func<bool> ready)
        {
            var timeout = Stopwatch.StartNew();
            do
            {
                streamed.UpdateStreaming(center);
                if (streamed.ChunkLifecycle.Error is Exception error) throw error;
                if (timeout.Elapsed.TotalSeconds > 20) throw new TimeoutException("Industrial streaming fixture stopped advancing.");
                Thread.Sleep(1);
            } while (!ready());
        }
    }
}
