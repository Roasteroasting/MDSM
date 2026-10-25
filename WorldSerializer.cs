using System;
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
    internal const int MaximumFileSize = World.BlockCount + 512;
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
        ValidatePose(save.PlayerPosition, save.Yaw, save.Pitch);
        using var payload = new MemoryStream(World.BlockCount + 512);
        using (var writer = new BinaryWriter(payload, Utf8, true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(World.Width); writer.Write(World.Height); writer.Write(World.Depth);
            writer.Write(save.World.Seed);
            byte[] nameBytes = Utf8.GetBytes(name);
            writer.Write(nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(save.PlayerPosition.X); writer.Write(save.PlayerPosition.Y); writer.Write(save.PlayerPosition.Z);
            writer.Write(save.Yaw); writer.Write(save.Pitch);
            writer.Write(World.BlockCount);
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
        // Bound allocation even for a malformed or non-seekable input stream.
        var bytes = new byte[MaximumFileSize + 1];
        int length = 0, read;
        while (length < bytes.Length && (read = source.Read(bytes.AsSpan(length))) > 0)
            length += read;
        if (length < World.BlockCount + 85 || length > MaximumFileSize)
            throw new InvalidDataException("Save file is truncated or has an invalid size.");
        int payloadLength = length - ChecksumSize;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, payloadLength)), bytes.AsSpan(payloadLength, ChecksumSize)))
            throw new InvalidDataException("Save checksum failed. The file is damaged or incomplete.");

        using var payload = new MemoryStream(bytes, 0, payloadLength, false);
        using var reader = new BinaryReader(payload, Utf8, true);
        WorldMetadata metadata = ReadHeader(reader);
        if (payload.Length - payload.Position != World.BlockCount)
            throw new InvalidDataException("Save block data length does not match the world dimensions.");
        var world = new World(metadata.Seed, false);
        foreach (Chunk chunk in world.Chunks)
        {
            ReadOnlySpan<byte> blocks = bytes.AsSpan((int)payload.Position, Chunk.BlockCount);
            ValidateBlocks(blocks);
            chunk.RestoreBlocks(blocks);
            payload.Position += Chunk.BlockCount;
        }
        return new WorldSave(world, metadata.Name, metadata.PlayerPosition, metadata.Yaw, metadata.Pitch);
    }

    // Listing reads only the small header. The complete checksum is checked when loading.
    public static WorldMetadata ReadMetadata(Stream source)
    {
        using var reader = new BinaryReader(source, Utf8, true);
        WorldMetadata metadata = ReadHeader(reader);
        if (source.CanSeek && source.Length - source.Position != World.BlockCount + ChecksumSize)
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
            if (reader.ReadInt32() != World.Width || reader.ReadInt32() != World.Height || reader.ReadInt32() != World.Depth)
                throw new InvalidDataException("Save dimensions must be 256 x 64 x 256.");
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
            ValidatePose(position, yaw, pitch);
            if (reader.ReadInt32() != World.BlockCount)
                throw new InvalidDataException("Save block count does not match the world dimensions.");
            return new WorldMetadata(name, seed, position, yaw, pitch);
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException)
        {
            throw new InvalidDataException("Save header contains invalid or incomplete data.", exception);
        }
    }

    private static void ValidatePose(Vector3 position, float yaw, float pitch)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || position.X < Player.HalfWidth || position.X > World.Width - Player.HalfWidth
            || position.Z < Player.HalfWidth || position.Z > World.Depth - Player.HalfWidth
            || position.Y < -16 || position.Y > World.Height + 16
            || !float.IsFinite(yaw) || MathF.Abs(yaw) > MathHelper.Pi
            || !float.IsFinite(pitch) || MathF.Abs(pitch) > Camera.PitchLimit)
            throw new InvalidDataException("Save contains an invalid player position or orientation.");
    }

    private static void ValidateBlocks(ReadOnlySpan<byte> blocks)
    {
        foreach (byte value in blocks)
            if (value != (byte)BlockType.Air && !Blocks.IsPlaceable((BlockType)value))
                throw new InvalidDataException($"Save contains unknown block ID {value}.");
    }
}
