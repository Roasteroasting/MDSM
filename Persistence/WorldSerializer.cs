using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static class WorldSerializer
{
    public const int FormatVersion = 7;
    public const int MaxNameLength = 48;
    private const uint Magic = 0x4d53444d; // ASCII MDSM in little-endian order.
    private const int ChecksumSize = 32;
    private const int MaxNameBytes = MaxNameLength * 4;
    private const int InventoryBytes = 8 + Inventory.SlotCount * 3;
    private const int TailPrefixBytes = InventoryBytes + 4;
    private const int DropBytes = 23;
    private const int PigBytes = 20;
    private const int MobBytes = PigBytes + 1; // Species tag followed by the existing pose/health fields.
    private const int RespawnBytes = 9;
    private const int FallingBytes = 25; // Block byte, integer X/Z, double Y/downward speed.
    private const int PopulationPrefixBytes = MobDefinitions.SpeciesCount * 4 + 4;
    internal const int MaximumFileSize = 512 * World.Height * 512 + 512 + TailPrefixBytes + DroppedItems.MaximumEntities * DropBytes
        + 8 + PassiveMobPopulation.MaximumMobs * MobBytes + PopulationPrefixBytes
        + PassiveMobPopulation.MaximumMobs * RespawnBytes + 4 + 512 * 512 * 4
        + 4 + FallingBlockSimulation.MaximumActiveBlocks * FallingBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string ValidateName(string name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name is "." or "..")
            throw new ArgumentException($"World name must contain 1-{MaxNameLength} characters.");
        foreach (char character in name)
            if (char.IsControl(character) || "/\\:*?\"<>|".Contains(character))
                throw new ArgumentException("World name cannot contain control characters or / \\ : * ? \" < > |.");
        return name;
    }

    public static void Write(Stream destination, WorldSave save)
    {
        string name = ValidateName(save.Name);
        ValidatePose(save.PlayerPosition, save.Yaw, save.Pitch, save.World.Size);
        ValidateHealth(save.Health);
        Inventory inventory = save.Inventory ?? Inventory.CreateStarter();
        DroppedItemState[] drops = save.DroppedItems ?? Array.Empty<DroppedItemState>();
        PigState[] pigs = save.Pigs ?? Array.Empty<PigState>();
        MobState[] mobs = save.Mobs ?? Array.Empty<MobState>();
        if (drops.Length > DroppedItems.MaximumEntities) throw new InvalidDataException("Too many dropped items.");
        if (pigs.Length + mobs.Length > PassiveMobPopulation.MaximumMobs) throw new InvalidDataException("Too many passive mobs.");
        MobPopulationState population = save.Population ?? MobPopulationState.FromLiving(pigs, mobs);
        population.Validate(MobPopulationState.LivingCounts(pigs, mobs));
        using var payload = new MemoryStream(save.World.BlockCount + 512 + drops.Length * DropBytes);
        using (var writer = new BinaryWriter(payload, Utf8, true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(save.World.Width); writer.Write(World.Height); writer.Write(save.World.Depth);
            writer.Write(save.World.Seed);
            byte[] nameBytes = Utf8.GetBytes(name);
            writer.Write(nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(save.PlayerPosition.X); writer.Write(save.PlayerPosition.Y); writer.Write(save.PlayerPosition.Z);
            writer.Write(save.Yaw); writer.Write(save.Pitch);
            writer.Write(save.World.BlockCount);
            foreach (Chunk chunk in save.World.Chunks)
            {
                ValidateBlocks(chunk.Blocks);
                writer.Write(chunk.Blocks);
            }
            writer.Write(inventory.SelectedSlot);
            writer.Write(Inventory.SlotCount);
            for (int slot = 0; slot < Inventory.SlotCount; slot++)
            {
                ItemStack stack = inventory[slot];
                writer.Write(stack.Item.Value);
                writer.Write((byte)stack.Count);
            }
            writer.Write(drops.Length);
            foreach (DroppedItemState drop in drops)
            {
                DroppedItems.Validate(drop, save.World.Size);
                writer.Write(drop.Item.Value); writer.Write((byte)drop.Count);
                writer.Write(drop.Position.X); writer.Write(drop.Position.Y); writer.Write(drop.Position.Z);
                writer.Write(drop.VerticalVelocity); writer.Write(drop.PickupDelay);
            }
            writer.Write(save.Health);
            writer.Write(pigs.Length + mobs.Length);
            foreach (PigState pig in pigs)
            {
                PigPopulation.Validate(pig, save.World.Size);
                writer.Write((byte)MobSpecies.Pig);
                writer.Write(pig.Position.X); writer.Write(pig.Position.Y); writer.Write(pig.Position.Z);
                writer.Write(pig.Yaw); writer.Write(pig.Health);
            }
            foreach (MobState mob in mobs)
            {
                if (mob.Species == MobSpecies.Pig) throw new InvalidDataException("Pigs must use the pig state collection.");
                PassiveMobPopulation.Validate(mob, save.World.Size);
                writer.Write((byte)mob.Species);
                writer.Write(mob.Position.X); writer.Write(mob.Position.Y); writer.Write(mob.Position.Z);
                writer.Write(mob.Yaw); writer.Write(mob.Health);
            }
            foreach (int target in population.Targets) writer.Write(target);
            writer.Write(population.Pending.Length);
            foreach (MobRespawnState respawn in population.Pending)
            { writer.Write((byte)respawn.Species); writer.Write(respawn.RemainingSeconds); }
            int[] columns = save.World.Gravity.CaptureColumns();
            writer.Write(columns.Length);
            foreach (int column in columns) writer.Write(column);
            FallingBlockState[] falling = save.World.Gravity.CaptureActive();
            writer.Write(falling.Length);
            foreach (FallingBlockState block in falling)
            { writer.Write((byte)block.Block); writer.Write(block.X); writer.Write(block.Z); writer.Write(block.Y); writer.Write(block.DownwardSpeed); }
        }
        ReadOnlySpan<byte> bytes = payload.GetBuffer().AsSpan(0, (int)payload.Length);
        destination.Write(bytes);
        destination.Write(SHA256.HashData(bytes));
    }

    public static WorldSave Read(Stream source)
    {
        try
        {
            // Validate a bounded header before allocating the exact selected world's payload.
            // This also works on non-seekable streams and rejects trailing or truncated data.
            byte[] prefix = new byte[28];
            source.ReadExactly(prefix);
            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(24));
            if (nameLength < 1 || nameLength > MaxNameBytes)
                throw new InvalidDataException("Save world name length is invalid.");
            byte[] header = new byte[52 + nameLength];
            prefix.CopyTo(header, 0);
            source.ReadExactly(header.AsSpan(prefix.Length));
            using var reader = new BinaryReader(new MemoryStream(header, false), Utf8);
            WorldMetadata metadata = ReadHeader(reader);
            int count = metadata.Size.BlockCount();
            byte[] bytes = new byte[count];
            source.ReadExactly(bytes);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(header);
            hash.AppendData(bytes);
            byte[] tail = Array.Empty<byte>();
            if (metadata.Version >= 2)
            {
                byte[] prefixTail = new byte[TailPrefixBytes];
                source.ReadExactly(prefixTail);
                int dropCount = BinaryPrimitives.ReadInt32LittleEndian(prefixTail.AsSpan(InventoryBytes));
                if (dropCount < 0 || dropCount > DroppedItems.MaximumEntities)
                    throw new InvalidDataException("Save has an invalid dropped-item count.");
                tail = new byte[TailPrefixBytes + dropCount * DropBytes + (metadata.Version >= 3 ? 4 : 0)];
                prefixTail.CopyTo(tail, 0);
                source.ReadExactly(tail.AsSpan(prefixTail.Length));
                hash.AppendData(tail);
            }
            PigState[] pigs = Array.Empty<PigState>();
            MobState[] mobs = Array.Empty<MobState>();
            if (metadata.Version >= 4)
            {
                byte[] countBytes = new byte[4];
                source.ReadExactly(countBytes); hash.AppendData(countBytes);
                int mobCount = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
                if (mobCount < 0 || mobCount > PassiveMobPopulation.MaximumMobs) throw new InvalidDataException("Invalid passive mob count.");
                byte[] mobBytes = new byte[mobCount * (metadata.Version >= 5 ? MobBytes : PigBytes)];
                source.ReadExactly(mobBytes); hash.AppendData(mobBytes);
                var pigList = new List<PigState>(); var otherList = new List<MobState>();
                using var mobReader = new BinaryReader(new MemoryStream(mobBytes, false));
                for (int i = 0; i < mobCount; i++)
                {
                    MobSpecies species = metadata.Version >= 5 ? (MobSpecies)mobReader.ReadByte() : MobSpecies.Pig;
                    var state = new MobState(species, new Vector3(mobReader.ReadSingle(), mobReader.ReadSingle(), mobReader.ReadSingle()),
                        mobReader.ReadSingle(), mobReader.ReadInt32());
                    PassiveMobPopulation.Validate(state, metadata.Size);
                    if (species == MobSpecies.Pig) pigList.Add(new PigState(state.Position, state.Yaw, state.Health));
                    else otherList.Add(state);
                }
                pigs = pigList.ToArray(); mobs = otherList.ToArray();
            }
            MobPopulationState population = null;
            int[] gravityColumns = Array.Empty<int>();
            FallingBlockState[] fallingBlocks = Array.Empty<FallingBlockState>();
            if (metadata.Version >= 6)
            {
                byte[] populationPrefix = new byte[PopulationPrefixBytes]; source.ReadExactly(populationPrefix); hash.AppendData(populationPrefix);
                int[] targets = new int[MobDefinitions.SpeciesCount];
                for (int i = 0; i < targets.Length; i++) targets[i] = BinaryPrimitives.ReadInt32LittleEndian(populationPrefix.AsSpan(i * 4));
                int pendingCount = BinaryPrimitives.ReadInt32LittleEndian(populationPrefix.AsSpan(targets.Length * 4));
                if (pendingCount < 0 || pendingCount > PassiveMobPopulation.MaximumMobs) throw new InvalidDataException("Invalid pending respawn count.");
                byte[] pendingBytes = new byte[pendingCount * RespawnBytes]; source.ReadExactly(pendingBytes); hash.AppendData(pendingBytes);
                var pending = new MobRespawnState[pendingCount];
                using var pendingReader = new BinaryReader(new MemoryStream(pendingBytes, false));
                for (int i = 0; i < pending.Length; i++) pending[i] = new MobRespawnState((MobSpecies)pendingReader.ReadByte(), pendingReader.ReadDouble());
                population = new MobPopulationState(targets, pending); population.Validate(MobPopulationState.LivingCounts(pigs, mobs));
                byte[] countBytes = new byte[4]; source.ReadExactly(countBytes); hash.AppendData(countBytes);
                int columnCount = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
                if (columnCount < 0 || columnCount > metadata.Size.Width() * metadata.Size.Width()) throw new InvalidDataException("Invalid gravity column count.");
                byte[] columnBytes = new byte[columnCount * 4]; source.ReadExactly(columnBytes); hash.AppendData(columnBytes);
                gravityColumns = new int[columnCount];
                for (int i = 0; i < columnCount; i++) gravityColumns[i] = BinaryPrimitives.ReadInt32LittleEndian(columnBytes.AsSpan(i * 4));
            }
            else population = MobPopulationState.FromLiving(pigs, mobs); // Conservative, once-only survivor baseline.
            if (metadata.Version >= 7)
            {
                byte[] countBytes = new byte[4]; source.ReadExactly(countBytes); hash.AppendData(countBytes);
                int fallingCount = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
                if (fallingCount < 0 || fallingCount > FallingBlockSimulation.MaximumActiveBlocks) throw new InvalidDataException("Invalid falling block count.");
                byte[] fallingBytes = new byte[fallingCount * FallingBytes]; source.ReadExactly(fallingBytes); hash.AppendData(fallingBytes);
                fallingBlocks = new FallingBlockState[fallingCount];
                using var fallingReader = new BinaryReader(new MemoryStream(fallingBytes, false));
                for (int i = 0; i < fallingCount; i++) fallingBlocks[i] = new FallingBlockState((BlockType)fallingReader.ReadByte(),
                    fallingReader.ReadInt32(), fallingReader.ReadInt32(), fallingReader.ReadDouble(), fallingReader.ReadDouble());
            }
            byte[] checksum = new byte[ChecksumSize];
            source.ReadExactly(checksum);
            if (source.ReadByte() != -1)
                throw new InvalidDataException("Save file contains extra block data.");
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), checksum))
                throw new InvalidDataException("Save checksum failed. The file is damaged or incomplete.");

            var world = new World(metadata.Seed, metadata.Size, false);
            int offset = 0;
            foreach (Chunk chunk in world.Chunks)
            {
                ReadOnlySpan<byte> blocks = bytes.AsSpan(offset, Chunk.BlockCount);
                ValidateBlocks(blocks);
                chunk.RestoreBlocks(blocks);
                offset += Chunk.BlockCount;
            }
            world.Fluids.Initialize();
            world.Gravity.Restore(gravityColumns, fallingBlocks); // No global scan/rewrite of legacy terrain.
            Inventory inventory;
            DroppedItemState[] drops;
            int health = Player.MaximumHealth;
            if (metadata.Version == 1)
            {
                inventory = Inventory.CreateStarter();
                drops = Array.Empty<DroppedItemState>();
            }
            else
            {
                using var extras = new BinaryReader(new MemoryStream(tail, false), Utf8);
                int selected = extras.ReadInt32();
                if (selected < 0 || selected >= Inventory.HotbarSlots || extras.ReadInt32() != Inventory.SlotCount)
                    throw new InvalidDataException("Save has an invalid inventory layout or selection.");
                inventory = new Inventory { SelectedSlot = selected };
                for (int slot = 0; slot < Inventory.SlotCount; slot++)
                {
                    ItemId item = new(extras.ReadUInt16());
                    int quantity = extras.ReadByte();
                    if (item.Value == 0 && quantity == 0) continue;
                    if (!Items.IsDefined(item) || quantity < 1 || quantity > ItemStack.Maximum)
                        throw new InvalidDataException("Save has an invalid inventory stack.");
                    inventory.SetSlot(slot, new ItemStack(item, quantity));
                }
                drops = new DroppedItemState[extras.ReadInt32()];
                for (int i = 0; i < drops.Length; i++)
                {
                    var drop = new DroppedItemState(new ItemId(extras.ReadUInt16()), extras.ReadByte(),
                        new Vector3(extras.ReadSingle(), extras.ReadSingle(), extras.ReadSingle()), extras.ReadSingle(), extras.ReadSingle());
                    DroppedItems.Validate(drop, metadata.Size);
                    drops[i] = drop;
                }
                if (metadata.Version >= 3) { health = extras.ReadInt32(); ValidateHealth(health); }
            }
            return new WorldSave(world, metadata.Name, metadata.PlayerPosition, metadata.Yaw, metadata.Pitch, inventory, drops, health, pigs, mobs, population);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Save file is truncated or has an invalid size.", exception);
        }
    }

    // Listing reads only the small header. The complete checksum is checked when loading.
    public static WorldMetadata ReadMetadata(Stream source)
    {
        using var reader = new BinaryReader(source, Utf8, true);
        WorldMetadata metadata = ReadHeader(reader);
        if (source.CanSeek)
        {
            long extra = source.Length - source.Position - metadata.Size.BlockCount() - ChecksumSize;
            if (metadata.Version >= 3) extra -= 4;
            if (metadata.Version >= 6)
            {
                long extension = PopulationPrefixBytes + 4;
                if (metadata.Version >= 7) extension += 4;
                if (extra < TailPrefixBytes + 4 + extension || extra > TailPrefixBytes + (long)DroppedItems.MaximumEntities * DropBytes
                    + 4 + (long)PassiveMobPopulation.MaximumMobs * (MobBytes + RespawnBytes) + extension + (long)metadata.Size.Width() * metadata.Size.Width() * 4
                    + (metadata.Version >= 7 ? FallingBlockSimulation.MaximumActiveBlocks * FallingBytes : 0))
                    throw new InvalidDataException("Save file has an invalid data length.");
            }
            else if (metadata.Version >= 4)
            {
                // Only the header is read for listings; validate variable tails fully on load.
                if (extra < TailPrefixBytes + 4 || extra > TailPrefixBytes + (long)DroppedItems.MaximumEntities * DropBytes
                    + 4 + (long)PassiveMobPopulation.MaximumMobs * (metadata.Version >= 5 ? MobBytes : PigBytes))
                    throw new InvalidDataException("Save file has an invalid data length.");
            }
            else if (metadata.Version == 1 ? extra != 0 : extra < TailPrefixBytes
                || (extra - TailPrefixBytes) % DropBytes != 0 || extra > TailPrefixBytes + (long)DroppedItems.MaximumEntities * DropBytes)
                throw new InvalidDataException("Save file has an invalid data length.");
        }
        return metadata;
    }

    private static WorldMetadata ReadHeader(BinaryReader reader)
    {
        try
        {
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("This is not an MDSM world save.");
            int version = reader.ReadInt32();
            if (version < 1 || version > FormatVersion)
                throw new InvalidDataException($"Save version {version} is unsupported; expected 1-{FormatVersion}.");
            WorldSize size = WorldSizes.FromDimensions(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            int seed = reader.ReadInt32();
            int nameLength = reader.ReadInt32();
            if (nameLength < 1 || nameLength > MaxNameBytes)
                throw new InvalidDataException("Save world name length is invalid.");
            byte[] nameBytes = reader.ReadBytes(nameLength);
            if (nameBytes.Length != nameLength)
                throw new InvalidDataException("Save header is incomplete.");
            string name = ValidateName(Utf8.GetString(nameBytes));
            Vector3 position = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float yaw = reader.ReadSingle(), pitch = reader.ReadSingle();
            ValidatePose(position, yaw, pitch, size);
            if (reader.ReadInt32() != size.BlockCount())
                throw new InvalidDataException("Save block count does not match the world dimensions.");
            return new WorldMetadata(name, seed, position, yaw, pitch, size, version);
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException)
        {
            throw new InvalidDataException("Save header contains invalid or incomplete data.", exception);
        }
    }

    private static void ValidatePose(Vector3 position, float yaw, float pitch, WorldSize size)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || position.X < Player.HalfWidth || position.X > size.Width() - Player.HalfWidth
            || position.Z < Player.HalfWidth || position.Z > size.Width() - Player.HalfWidth
            || position.Y < -16 || position.Y > World.Height + 16
            || !float.IsFinite(yaw) || MathF.Abs(yaw) > MathHelper.Pi
            || !float.IsFinite(pitch) || MathF.Abs(pitch) > Camera.PitchLimit)
            throw new InvalidDataException("Save contains an invalid player position or orientation.");
    }

    private static void ValidateBlocks(ReadOnlySpan<byte> blocks)
    {
        foreach (byte value in blocks)
            if (!Blocks.IsDefined((BlockType)value))
                throw new InvalidDataException($"Save contains unknown block ID {value}.");
    }

    private static void ValidateHealth(int health)
    {
        if (health < 0 || health > Player.MaximumHealth) throw new InvalidDataException("Save health must be an integer from 0 to 20.");
    }
}
