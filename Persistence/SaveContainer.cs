using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    internal const int ChunksPerGroup=32, RecordHeaderBytes=56;
    private const int ContainerSchema=1, ContainerOptionsBytes=16;
    internal const uint EndRecord=0, CoreHeaderRecord=1, ChunkGroupRecord=2, GameStateRecord=3, EnvironmentRecord=4;
    internal const ushort RequiredRecord=1;
    private const long MaximumContainerSize=MaximumFileSize+4*1024*1024;

    public static void Write(Stream destination,WorldSave save)=>WriteCompressed(destination,save,SaveCodec.Zstandard);
    internal static void WriteCompressed(Stream destination,WorldSave save,SaveCodec codec)
    {
        byte[] header=ContainerHeader(save,FormatVersion);
        Span<byte> options=stackalloc byte[ContainerOptionsBytes];
        BinaryPrimitives.WriteInt32LittleEndian(options,ContainerSchema);
        BinaryPrimitives.WriteInt32LittleEndian(options[4..],ChunksPerGroup);
        BinaryPrimitives.WriteInt32LittleEndian(options[8..],SaveCompression.FrameBytes);
        BinaryPrimitives.WriteInt32LittleEndian(options[12..],(int)codec);
        using var whole=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var target=new HashedWriteStream(destination,whole);
        target.Write(header);target.Write(options);
        using(var checksum=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {checksum.AppendData(header);checksum.AppendData(options);target.Write(checksum.GetHashAndReset());}
        using var frames=new CompressedWriteStream(target,header.Length,save.World.BlockCount,codec);
        WriteLegacy(frames,save);frames.Finish(save.World);
        destination.Write(whole.GetHashAndReset());
    }
    private static byte[] ContainerHeader(WorldSave save,int version)
    {
        string name=ValidateName(save.Name);ValidatePose(save.PlayerPosition,save.Yaw,save.Pitch,save.World.Size);
        using var data=new MemoryStream();using var writer=new BinaryWriter(data,Utf8,true);
        writer.Write(Magic);writer.Write(version);writer.Write(save.World.Width);writer.Write(World.Height);writer.Write(save.World.Depth);
        writer.Write(save.World.Seed);byte[] nameBytes=Utf8.GetBytes(name);writer.Write(nameBytes.Length);writer.Write(nameBytes);
        writer.Write(save.PlayerPosition.X);writer.Write(save.PlayerPosition.Y);writer.Write(save.PlayerPosition.Z);writer.Write(save.Yaw);writer.Write(save.Pitch);writer.Write(save.World.BlockCount);
        return data.ToArray();
    }
    public static WorldSave Read(Stream source)
    {
        try
        {
            using var replay=Replay(source,out int version);
            if(version<11)return ReadLegacy(replay);
            if(replay.CanSeek && replay.Length-replay.Position>MaximumContainerSize)throw new InvalidDataException("Compressed save exceeds the size limit.");
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var hashed=new HashedReadStream(replay,hash);
            var info=ReadContainerHeader(hashed);
            ValidateContainerRemainder(replay,info.Metadata);
            using var frames=new CompressedReadStream(hashed,replay,hash,info.Header,info.Metadata);
            WorldSave save=ReadLegacy(frames);
            if(frames.Environment is WorldEnvironment state)
            {save.World.Time.Restore(state.Seconds);save.World.SavedGameMode=state.Mode;}
            return save;
        }
        catch(EndOfStreamException e){throw new InvalidDataException("Compressed save is truncated or incomplete.",e);}
    }
    public static WorldMetadata ReadMetadata(Stream source)
    {
        try
        {
            using var replay=Replay(source,out int version);
            return version>=11?ReadContainerHeader(replay).Metadata:ReadLegacyMetadata(replay);
        }
        catch(EndOfStreamException e){throw new InvalidDataException("Save header is truncated.",e);}
    }
    private static (byte[] Header,WorldMetadata Metadata) ReadContainerHeader(Stream source)
    {
        if(source.CanSeek && source.Length-source.Position>MaximumContainerSize)throw new InvalidDataException("Compressed save exceeds the supported size limit.");
        byte[] prefix=new byte[28];source.ReadExactly(prefix);int length=BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(24));
        if(length<1 || length>MaxNameBytes)throw new InvalidDataException("Invalid compressed save name length.");
        byte[] header=new byte[52+length];prefix.CopyTo(header,0);source.ReadExactly(header.AsSpan(28));
        using var reader=new BinaryReader(new MemoryStream(header,false),Utf8);
        var metadata=ReadHeader(reader);
        if(metadata.Version is <11 or >FormatVersion || metadata.StoredHeight!=World.Height)throw new InvalidDataException("Unsupported compressed save dimensions/version.");
        Span<byte> options=stackalloc byte[ContainerOptionsBytes];source.ReadExactly(options);
        if(BinaryPrimitives.ReadInt32LittleEndian(options)!=ContainerSchema || BinaryPrimitives.ReadInt32LittleEndian(options[4..])!=ChunksPerGroup
            || BinaryPrimitives.ReadInt32LittleEndian(options[8..])!=SaveCompression.FrameBytes || BinaryPrimitives.ReadInt32LittleEndian(options[12..]) is <0 or >2)
            throw new InvalidDataException("Unsupported compressed save layout or codec.");
        Span<byte> expected=stackalloc byte[32];source.ReadExactly(expected);
        using var checksum=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);checksum.AppendData(header);checksum.AppendData(options);
        if(!CryptographicOperations.FixedTimeEquals(expected,checksum.GetHashAndReset()))throw new InvalidDataException("Save metadata checksum failed.");
        ValidateContainerRemainder(source,metadata);
        // The inner, unchanged v10 payload must repeat this exact validated metadata.
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),LegacyFormatVersion);
        return(header,metadata);
    }
    private static void ValidateContainerRemainder(Stream source,WorldMetadata metadata)
    {
        long groups=(metadata.StoredBlockCount/Chunk.BlockCount+ChunksPerGroup-1)/ChunksPerGroup;
        // Header-only listing can reject gross truncation without reading/decompressing
        // payloads. Full record/checksum validation still happens on load.
        if(source.CanSeek && source.Length-source.Position<(groups+3)*RecordHeaderBytes+ChecksumSize)
            throw new InvalidDataException("Compressed save is missing required records.");
    }
    // Seekable streams retain their length checks. Pipes get an eight-byte replay
    // prefix; neither path buffers the complete file or takes ownership of it.
    private static Stream Replay(Stream source,out int version)
    {
        byte[] prefix=new byte[8];long start=source.CanSeek?source.Position:0;source.ReadExactly(prefix);
        version=BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(4));
        if(source.CanSeek){source.Position=start;return new BorrowedStream(source);}
        return new PrefixStream(prefix,source);
    }
    private abstract class ForwardStream : Stream
    {
        public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Flush(){ }
    }
    private sealed class BorrowedStream(Stream source) : Stream
    {
        public override bool CanRead=>source.CanRead;public override bool CanWrite=>false;public override bool CanSeek=>source.CanSeek;
        public override long Length=>source.Length;public override long Position{get=>source.Position;set=>source.Position=value;}
        public override int Read(byte[] b,int o,int n)=>source.Read(b,o,n);public override int Read(Span<byte> b)=>source.Read(b);public override int ReadByte()=>source.ReadByte();
        public override long Seek(long o,SeekOrigin s)=>source.Seek(o,s);public override void Flush(){ }
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
    private sealed class PrefixStream(byte[] prefix,Stream source) : ForwardStream
    {
        private int _position;
        public override bool CanRead=>true;public override bool CanWrite=>false;
        public override int Read(byte[] b,int o,int n)=>Read(b.AsSpan(o,n));
        public override int Read(Span<byte> buffer)
        {if(_position>=prefix.Length)return source.Read(buffer);int count=Math.Min(buffer.Length,prefix.Length-_position);prefix.AsSpan(_position,count).CopyTo(buffer);_position+=count;return count;}
        public override int ReadByte(){Span<byte> b=stackalloc byte[1];return Read(b)==0?-1:b[0];}
        public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
}
