using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "MDSM.csproj")))
{
    Console.Error.WriteLine("Pass the MDSM project directory, for example: dotnet run --project Tools/WikiExport -- .");
    return 1;
}

string root = Path.GetFullPath(args[0]);
string wiki = Path.Combine(root, "Wiki");
Directory.CreateDirectory(Path.Combine(wiki, "assets"));
string Name(string raw) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.ToLowerInvariant());
var allBlocks = Enumerable.Range(0, Blocks.MaximumId + 1).Select(id => (BlockType)id).ToArray();
string BlockName(BlockType block)
{
    if (Water.IsWater(block) || Lava.IsLava(block))
    {
        string liquid = Lava.IsLava(block) ? "Lava" : "Water";
        string state = block.ToString()[liquid.Length..];
        return state=="Source" ? liquid + " Source" : liquid + (state.StartsWith("Flow") ? " Flow " + state[4..] : " Falling " + (state.Length==7 ? "0" : state[7..]));
    }
    if (Blocks.Crop(block)) return $"{Name(Blocks.Get(block).Name)} (Stage {Blocks.CropStage(block)+1})";
    return block switch
    {
        BlockType.DoorUpper => "Wooden Door (Upper Half)",
        BlockType.BedHead => "Bed (Head)",
        BlockType.DoubleTallGrassLower => "Double Tall Grass (Lower Half)",
        BlockType.DoubleTallGrassUpper => "Double Tall Grass (Upper Half)",
        _ => Name(Blocks.Get(block).Name)
    };
}

var tintMethod = typeof(Blocks).Assembly.GetType("MDSM.Worlds.DyePalette")?
    .GetMethod("Tint",BindingFlags.NonPublic|BindingFlags.Static)
    ?? throw new InvalidOperationException("Dye palette lookup changed.");
var blocks = allBlocks.Select(block =>
{
    var d = Blocks.Get(block);
    var mining = MiningRules.Profile(block);
    int tile = block == BlockType.Air ? -1 : d.FrontTile >= 0 ? d.FrontTile : d.TopTile >= 0 ? d.TopTile : d.SideTile;
    var tint = (Microsoft.Xna.Framework.Color)tintMethod.Invoke(null,new object[]{block})!;
    return new {id=(int)block, key=block.ToString(), name=BlockName(block), d.Solid,d.Opaque,d.Placeable,
        tint=tint==Microsoft.Xna.Framework.Color.White?null:$"#{tint.R:x2}{tint.G:x2}{tint.B:x2}",
        shape=d.Shape.ToString(), tile, breakable=d.MiningSeconds>=0, tool=mining.Category switch
        {MiningCategory.Mineral=>"Pickaxe",MiningCategory.Earth=>"Shovel",MiningCategory.Wood=>"Axe",_=>""},
        tier=mining.Required.ToString(), falls=Blocks.Falls(block), stage=Blocks.Crop(block)?Blocks.CropStage(block)+1:0};
}).ToArray();
var items = Items.Obtainable.ToArray().Select(item =>
{
    ItemDefinition d = Items.Get(item);
    return new { id=(int)item.Value, name=Name(d.Name), d.StackLimit, d.Durability, d.Sprite,
        block=Items.TryGetBlock(item,out var block)?(int?)block:null,
        tool=d.Tool.ToString(), tier=d.Tier.ToString(), armor=d.Armor.ToString(), d.ArmorPoints, d.AttackDamage,
        lavaResistant=Items.LavaResistant(item) };
}).ToArray();

// Read the exact ingredient grid without changing the gameplay API.
var ingredientsField = typeof(ShapedRecipe).GetField("_ingredients", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("Crafting ingredient layout changed.");
var recipes = CraftingRecipes.All.ToArray().Select((recipe,index) => new
{
    id=index+1, recipe.Width,recipe.Height, recipe.Shapeless, station=recipe.Station.ToString(),
    output=(int)recipe.Output.Item.Value, count=recipe.Output.Count,
    cells=((CraftingIngredient[])ingredientsField.GetValue(recipe)!).Select(cell=>new {id=(int)cell.Item.Value, count=cell.Count}).ToArray()
}).ToArray();
var smelting = Smelting.Recipes.Select(recipe => new {input=(int)recipe.Input.Value, output=(int)recipe.Output.Value,
    machines=allBlocks.Where(block=>Smelting.Machine(block) && Smelting.Result(recipe.Input,block)==recipe.Output)
        .Select(block=>new {block=(int)block,seconds=Smelting.Seconds/Smelting.Speed(block)}).ToArray()
}).ToArray();
var fuels = Items.Obtainable.ToArray().Where(item=>Smelting.Fuel(item)>0)
    .Select(item=>new {id=(int)item.Value, seconds=Smelting.Fuel(item)}).ToArray();
var mobs = Enum.GetValues<MobSpecies>().Select(species =>
{
    var mob=MobDefinitions.Get(species);
    return new {id=(int)species, name=species.ToString(), health=mob.MaximumHealth, speed=mob.WalkSpeed,
        drop=(int)mob.Drop.Value, mob.MaximumDrop, hostile=species>=MobSpecies.Zombie};
}).ToArray();
var itemIds = items.Select(item=>item.id).ToHashSet();
if (recipes.Any(r=>!itemIds.Contains(r.output)||r.cells.Any(c=>c.id!=0&&!itemIds.Contains(c.id)))
    || smelting.Any(r=>r.machines.Length==0||!itemIds.Contains(r.input)||!itemIds.Contains(r.output))
    || mobs.Any(m=>!itemIds.Contains(m.drop)))
    throw new InvalidOperationException("A wiki recipe or mob drop refers to an unknown item.");

var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
File.WriteAllText(Path.Combine(wiki,"data.js"), "globalThis.MDSM_WIKI = " +
    JsonSerializer.Serialize(new {blocks,items,recipes,smelting,fuels,mobs},options) + ";\n");
foreach(var (source,target) in new[] {("Blocks/terrain.png","terrain.png"),("Items/items.png","items.png"),("UI/Panorama/0.png","world.png"),("Blocks/water.png","water.png")})
    File.Copy(Path.Combine(root,"Content",source), Path.Combine(wiki,"assets",target), true);
Console.WriteLine($"Exported {blocks.Length} block IDs, {items.Length} inventory entries, {recipes.Length} crafting recipes and {smelting.Length} smelting recipes.");
return 0;
