using System;
using System.Globalization;
using System.Linq;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Gameplay;

internal readonly record struct CommandResult(bool Success, string Message, bool SettingsChanged = false);

// Commands operate through existing gameplay owners; no direct save/mesh manipulation.
internal sealed class WorldCommands
{
    public const int MaximumGive = Inventory.SlotCount * ItemStack.Maximum, MaximumSpawn = 32;
    private readonly Random _random = new();
    private const string Overview = "/help [give|spawn|kill|time|gamemode|items [page]|mobs]\n/give @p <item> [quantity]\n/spawn <mob> [quantity]\n/kill @p OR /kill @e [type=<entity>]\n/time set <0-23999|day|noon|sunset|night|midnight|sunrise>\n/time query\n/gamemode <survival|creative>";
    private static CommandResult Error(string text) => new(false, text);
    public CommandResult Execute(string text, GameSession session, GameSettings settings)
    {
        if (session == null || session.Player.IsDead) return Error("Commands require a living player in a world.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256 || !text.TrimStart().StartsWith('/')) return Error("Commands begin with /. Use /help.");
        string[] args = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        string command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "/help": return Help(args);
            case "/kill": return Kill(args, session);
            case "/give":
                if (args.Length is < 3 or > 4 || !args[1].Equals("@p", StringComparison.OrdinalIgnoreCase)) return Error("Usage: /give @p <item name_or_ID> [quantity 1-2304]");
                if (!TryItem(args[2], out ItemId item)) return Error("Unknown item. Use /help items [page] for names and IDs.");
                if (!Quantity(args, 3, MaximumGive, out int quantity)) return Error("Give quantity must be a whole number from 1 to 2304.");
                int capacity = 0, limit = Items.StackLimit(item);
                for (int i = 0; i < Inventory.SlotCount; i++)
                {
                    ItemStack stack = session.Inventory[i];
                    if (stack.IsEmpty) capacity += limit;
                    else if (stack.Item == item && limit > 1) capacity += limit - stack.Count;
                }
                if (capacity < quantity) return Error($"Not enough inventory space: room for {capacity}. Nothing given.");
                session.Inventory.Add(item, quantity); // Preflight guarantees exact insertion, with fresh equipment identities.
                return new(true, $"Given {quantity} {Items.Name(item)}.");
            case "/spawn":
                if (args.Length is < 2 or > 3) return Error("Usage: /spawn <mob name_or_ID> [quantity 1-32]");
                if (!TryMob(args[1], out MobSpecies species)) return Error("Unknown mob. Use /help mobs for names and IDs.");
                if (!Quantity(args, 2, MaximumSpawn, out int count)) return Error("Spawn quantity must be a whole number from 1 to 32.");
                if (species >= MobSpecies.Zombie && settings.Difficulty == Difficulty.Peaceful) return Error("Hostile mobs cannot spawn on Peaceful difficulty.");
                int spawned = 0;
                for (; spawned < count; spawned++)
                {
                    if (species < MobSpecies.Zombie ? session.Mobs.Entities.Count + session.Mobs.PendingRespawns.Count >= PassiveMobPopulation.MaximumMobs : session.Hostiles.Entities.Count >= HostilePopulation.Maximum) break;
                    if (!MobCommandPlacement.TryFind(session, species, _random, out var feet)) break;
                    if (!(species < MobSpecies.Zombie ? session.Mobs.TrySummon(species, feet) : session.Hostiles.TrySummon(species, feet))) break;
                }
                return new(spawned > 0, $"Spawned {spawned}/{count} {species}." + (spawned < count ? " No more safe nearby space or population limit reached." : ""));
            case "/time":
                if (args.Length == 2 && args[1].Equals("query", StringComparison.OrdinalIgnoreCase))
                    return new(true, $"Time: {(int)Math.Floor(session.World.Time.Seconds / WorldTime.DaySeconds * 24000 + .0000001)} / 24000 (0 = sunrise).");
                if (args.Length != 3 || !args[1].Equals("set", StringComparison.OrdinalIgnoreCase)) return Error("Usage: /time set <0-23999|day|noon|sunset|night|midnight|sunrise> OR /time query");
                int ticks = args[2].ToLowerInvariant() switch { "sunrise" => 0, "day" => 1000, "noon" => 6000, "sunset" => 12000, "night" => 14000, "midnight" => 18000, _ => -1 };
                if (ticks < 0 && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks is < 0 or > 23999)) return Error("Time must be a whole number 0-23999 or a named preset. Use /help time.");
                session.World.Time.Restore(ticks * (WorldTime.DaySeconds / 24000));
                return new(true, $"Time set to {ticks} / 24000.");
            case "/gamemode":
                if (args.Length != 2 || !(args[1].Equals("survival", StringComparison.OrdinalIgnoreCase) || args[1].Equals("creative", StringComparison.OrdinalIgnoreCase))) return Error("Usage: /gamemode <survival|creative>");
                settings.GameMode = args[1].Equals("creative", StringComparison.OrdinalIgnoreCase) ? GameMode.Creative : GameMode.Survival;
                session.ApplySettings(settings);
                return new(true, $"Game mode: {settings.GameMode}.", true);
            default: return Error("Unknown command. Use /help.");
        }
    }
    private static CommandResult Kill(string[] args, GameSession session)
    {
        const string usage = "Usage: /kill @p OR /kill @e [type=mob|passive|hostile|player|item|arrow|falling_block|species name_or_ID]";
        if (args.Length is < 2 or > 3) return Error(usage);
        bool playerSelector = args[1].Equals("@p", StringComparison.OrdinalIgnoreCase);
        if (playerSelector && args.Length != 2 || !playerSelector && !args[1].Equals("@e", StringComparison.OrdinalIgnoreCase)) return Error(usage);
        string type = playerSelector ? "player" : "all";
        MobSpecies? species = null;
        if (args.Length == 3)
        {
            if (!args[2].StartsWith("type=", StringComparison.OrdinalIgnoreCase)) return Error(usage);
            type = args[2][5..].ToLowerInvariant();
            if (TryMob(type, out MobSpecies selected)) species = selected;
            else if (type is not ("mob" or "passive" or "hostile" or "player" or "item" or "arrow" or "falling_block")) return Error("Unknown entity type. Use /help kill.");
        }
        bool all = type == "all";
        int count = 0, failed = 0;
        // Free pre-existing item capacity first for an all-entity wipe, then use
        // ordinary mob deaths so natural population accounting stays correct.
        if (all || type == "item") count += session.Items.RemoveAll();
        if (all || type == "arrow") count += session.Projectiles.RemoveAll();
        if (all || type == "falling_block") count += session.World.Gravity.RemoveActive();
        if (all || type is "mob" or "passive" || species < MobSpecies.Zombie)
            for (int i = session.Mobs.Entities.Count - 1; i >= 0; i--)
            {
                PassiveMob mob = session.Mobs.Entities[i];
                if (species.HasValue && mob.Species != species.Value) continue;
                if (session.Mobs.Hit(mob, mob.Health, session.Player.Position, session.Items)) count++; else failed++;
            }
        if (all || type is "mob" or "hostile" || species >= MobSpecies.Zombie)
            for (int i = session.Hostiles.Entities.Count - 1; i >= 0; i--)
            {
                PassiveMob mob = session.Hostiles.Entities[i];
                if (species.HasValue && mob.Species != species.Value) continue;
                if (session.Hostiles.Hit(mob, mob.Health, session.Player.Position, session.Items)) count++; else failed++;
            }
        if (all || type == "player")
        {
            if (session.Player.Kill()) count++;
            session.HandleDeath(); // Idempotent, including cursor, crafting and armor.
        }
        if (all) session.Items.RemoveAll(); // Also remove the just-created death loot.
        return new(failed == 0, $"Killed/removed {count} entities." + (failed > 0 ? $" {failed} mobs retained: no room for their drops." : ""));
    }
    private static bool Quantity(string[] args, int index, int maximum, out int count)
    { count = 1; return args.Length <= index || int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 1 && count <= maximum; }
    internal static string Key(string name) => name.Replace(" ", "_").ToLowerInvariant();
    internal static bool TryItem(string token, out ItemId item)
    {
        if (ushort.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id)) { item = new(id); return Items.IsDefined(item); }
        foreach (ItemId candidate in Items.Obtainable)
            if (Key(Items.Name(candidate)).Equals(token, StringComparison.OrdinalIgnoreCase)
                || Items.TryGetBlock(candidate, out BlockType block) && block.ToString().Equals(token, StringComparison.OrdinalIgnoreCase)) { item = candidate; return true; }
        item = default; return false;
    }
    private static bool TryMob(string token, out MobSpecies species)
    {
        if (byte.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out byte id))
        { species = (MobSpecies)id; return MobDefinitions.IsDefined(species); }
        foreach (MobSpecies candidate in Enum.GetValues<MobSpecies>())
            if (candidate.ToString().Equals(token, StringComparison.OrdinalIgnoreCase)) { species = candidate; return true; }
        species = default; return false;
    }
    private static CommandResult Help(string[] args)
    {
        if (args.Length == 1) return new(true, Overview);
        string topic = args[1].ToLowerInvariant();
        if (topic == "items")
        {
            const int perPage = 4;
            int pages = (Items.Obtainable.Length + perPage - 1) / perPage, page = 1;
            if (args.Length > 3 || args.Length == 3 && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1 || page > pages)) return Error($"Usage: /help items [page 1-{pages}]");
            string listing = $"ITEMS {page}/{pages}: use names with underscores or IDs";
            foreach (ItemId item in Items.Obtainable.Slice((page - 1) * perPage, Math.Min(perPage, Items.Obtainable.Length - (page - 1) * perPage))) listing += $"\n{item.Value}: {Key(Items.Name(item))}";
            return new(true, listing);
        }
        if (args.Length != 2) return Error("Usage: /help [give|spawn|kill|time|gamemode|items [page]|mobs]");
        return topic switch
        {
            "give" => new(true, "/give @p <item name_or_ID> [quantity 1-2304]\nExample: /give @p stone 64\nUse underscores in names, e.g. diamond_pickaxe.\nAll items must fit. Equipment arrives at full durability."),
            "spawn" => new(true, "/spawn <mob name_or_ID> [quantity 1-32]\nExample: /spawn sheep 3\nFinds safe nearby ground; respects entity limits and Peaceful.\nExplicit passive mobs do not increase respawn targets."),
            "kill" => new(true, "/kill @p - Kill yourself, including in Creative.\n/kill @e - Remove ALL entities, including death loot.\n/kill @e type=<entity> - Filter targets.\nTypes: mob, passive, hostile, player, item, arrow,\nfalling_block, or a mob species name/ID (/help mobs).\nExample: /kill @e type=pig\nFiltered mob kills keep normal drops and respawn rules.\nItem/falling-block removal permanently deletes those entities."),
            "mobs" => new(true, string.Join("\n", Enum.GetValues<MobSpecies>().Select(s => $"{(byte)s}: {s.ToString().ToLowerInvariant()}"))),
            "time" => new(true, "/time set <0-23999|sunrise|day|noon|sunset|night|midnight>\n/time query\n0 sunrise, 1000 day, 6000 noon, 12000 sunset,\n14000 night, 18000 midnight. 20 ticks = 1 gameplay second."),
            "gamemode" => new(true, "/gamemode <survival|creative>\nChanges the same mode setting used by Settings.\nPreserves inventory and equipment."),
            _ => Error("Unknown help topic. Use /help.")
        };
    }
}
