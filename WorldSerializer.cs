using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace MDSM;

public static class WorldSerializer
{
    public const int FormatVersion = 1;
    public const int MaxNameLength = 48;
    private const uint Magic = 0x4d53444d; // ASCII MDSM in little-endian order.
    private const int ChecksumSize = 32;
    private const int MaxNameBytes = MaxNameLength * 4;
    internal const int MaximumFileSize = 512 * World.Height * 512 + 512;
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
        using var payload = new MemoryStream(save.World.BlockCount + 512);
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
            byte[] bytes = new byte[header.Length + count];
            header.CopyTo(bytes, 0);
            source.ReadExactly(bytes.AsSpan(header.Length));
            byte[] checksum = new byte[ChecksumSize];
            source.ReadExactly(checksum);
            if (source.ReadByte() != -1)
                throw new InvalidDataException("Save file contains extra block data.");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), checksum))
                throw new InvalidDataException("Save checksum failed. The file is damaged or incomplete.");

            var world = new World(metadata.Seed, metadata.Size, false);
            int offset = header.Length;
            foreach (Chunk chunk in world.Chunks)
            {
                ReadOnlySpan<byte> blocks = bytes.AsSpan(offset, Chunk.BlockCount);
                ValidateBlocks(blocks);
                chunk.RestoreBlocks(blocks);
                offset += Chunk.BlockCount;
            }
            return new WorldSave(world, metadata.Name, metadata.PlayerPosition, metadata.Yaw, metadata.Pitch);
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
        if (source.CanSeek && source.Length - source.Position != metadata.Size.BlockCount() + ChecksumSize)
            throw new InvalidDataException("Save file has an invalid block data length.");
        return metadata;
    }

    private static WorldMetadata ReadHeader(BinaryReader reader)
    {
        try
        {
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("This is not an MDSM world save.");
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Save version {version} is unsupported; expected {FormatVersion}.");
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
            return new WorldMetadata(name, seed, position, yaw, pitch, size);
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
}
