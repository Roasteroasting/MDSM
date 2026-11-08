using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    private static void RecordHeader(Span<byte> header,uint tag,int sequence,ReadOnlySpan<byte> raw,int stored,SaveCodec codec)
    {
        header.Clear();BinaryPrimitives.WriteUInt32LittleEndian(header,tag);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..],1);BinaryPrimitives.WriteUInt16LittleEndian(header[6..],RequiredRecord);
        header[8]=(byte)codec;BinaryPrimitives.WriteInt32LittleEndian(header[12..],sequence);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..],raw.Length);BinaryPrimitives.WriteInt32LittleEndian(header[20..],stored);
        SHA256.HashData(raw,header[24..]);
    }
    private sealed class CompressedWriteStream(Stream target,int headerBytes,int blockBytes,SaveCodec codec) : ForwardStream
    {
        private readonly byte[] _raw=new byte[SaveCompression.FrameBytes],_packed=new byte[SaveCompression.PackedLimit];
        private readonly SaveCompression _compression=new();
        private int _phase,_used,_chunks,_stateFrames;
        private long _written;
        private bool _finished;
        public override bool CanRead=>false;public override bool CanWrite=>true;
        public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int n)=>Write(b.AsSpan(o,n));
        public override void Write(ReadOnlySpan<byte> source)
        {
            if(_finished)throw new InvalidOperationException("Save already finished.");
            while(!source.IsEmpty)
            {
                int limit=_phase==0?headerBytes:_phase==1?(int)Math.Min(_raw.Length,headerBytes+(long)blockBytes-_written+_used):_raw.Length;
                int count=Math.Min(source.Length,limit-_used);source[..count].CopyTo(_raw.AsSpan(_used));_used+=count;_written+=count;source=source[count..];
                if(_written>MaximumFileSize)throw new InvalidDataException("Decoded save exceeds the size limit.");
                if(_used==limit)
                {
                    if(_phase==0){Emit(CoreHeaderRecord,0,SaveCodec.Stored);_phase=1;}
                    else if(_phase==1){int chunks=_used/Chunk.BlockCount;Emit(ChunkGroupRecord,_chunks,codec);_chunks+=chunks;if(_written==headerBytes+(long)blockBytes)_phase=2;}
                    else Emit(GameStateRecord,_stateFrames++,codec);
                }
            }
        }
        private void Emit(uint tag,int sequence,SaveCodec requested)
        {
            var input=_raw.AsSpan(0,_used);int length=requested==SaveCodec.Stored?_used:_compression.Compress(requested,input,_packed);
            SaveCodec actual=length>=_used?SaveCodec.Stored:requested;ReadOnlySpan<byte> payload=actual==SaveCodec.Stored?input:_packed.AsSpan(0,length);
            Span<byte> header=stackalloc byte[RecordHeaderBytes];RecordHeader(header,tag,sequence,input,payload.Length,actual);
            target.Write(header);target.Write(payload);_used=0;
        }
        public void Finish(WorldSave save)
        {
            World world=save.World;
            if(_phase!=2)throw new InvalidDataException("Incomplete world payload.");
            if(_used>0)Emit(GameStateRecord,_stateFrames++,codec);
            BinaryPrimitives.WriteDoubleLittleEndian(_raw,world.Time.Seconds);
            var mode=world.SavedGameMode??MDSM.Core.GameMode.Survival;
            if(!Enum.IsDefined(mode) || !Enum.IsDefined(world.SavedDifficulty))throw new InvalidDataException("Invalid world rules.");
            _raw[8]=(byte)mode;
            _raw[9]=1; // Lighting rules version. Packed light is derived lazily, not duplicated on disk.
            _raw[10]=world.AllowCheats?(byte)1:(byte)0;
            _raw[11]=(byte)world.SavedDifficulty;
            _used=12;Emit(EnvironmentRecord,0,codec);
            byte[] features=WriteFeatures(save);int sequence=0;
            for(int offset=0;offset<features.Length;offset+=_raw.Length)
            {_used=Math.Min(_raw.Length,features.Length-offset);features.AsSpan(offset,_used).CopyTo(_raw);Emit(FeaturesRecord,sequence++,codec);}
            Span<byte> end=stackalloc byte[RecordHeaderBytes];RecordHeader(end,EndRecord,0,ReadOnlySpan<byte>.Empty,0,SaveCodec.Stored);target.Write(end);_finished=true;
        }
        protected override void Dispose(bool disposing){if(disposing)_compression.Dispose();base.Dispose(disposing);}
    }
    private sealed class CompressedReadStream(Stream hashed,Stream source,IncrementalHash whole,byte[] expectedHeader,WorldMetadata metadata) : ForwardStream
    {
        private readonly byte[] _raw=new byte[SaveCompression.FrameBytes],_packed=new byte[SaveCompression.PackedLimit];
        private readonly SaveCompression _compression=new();
        private int _position,_length,_phase,_chunks,_states,_records;
        private long _decoded,_encoded;
        private bool _ended;
        internal WorldEnvironment? Environment { get; private set; }
        private readonly MemoryStream _features=new();
        private int _featureFrames;
        internal byte[] Features=>_featureFrames==0?null:_features.ToArray();
        public override bool CanRead=>true;public override bool CanWrite=>false;
        public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
        public override int Read(byte[] b,int o,int n)=>Read(b.AsSpan(o,n));
        public override int Read(Span<byte> buffer)
        {
            if(buffer.IsEmpty)return 0;
            while(_position==_length){if(_ended)return 0;Next();}
            int count=Math.Min(buffer.Length,_length-_position);_raw.AsSpan(_position,count).CopyTo(buffer);_position+=count;return count;
        }
        public override int ReadByte(){Span<byte> b=stackalloc byte[1];return Read(b)==0?-1:b[0];}
        private void Next()
        {
            while(true)
            {
                Span<byte> header=stackalloc byte[RecordHeaderBytes];hashed.ReadExactly(header);
                if(++_records>2048)throw new InvalidDataException("Too many compressed save records.");
                uint tag=BinaryPrimitives.ReadUInt32LittleEndian(header);ushort version=BinaryPrimitives.ReadUInt16LittleEndian(header[4..]),flags=BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
                var codec=(SaveCodec)header[8];int sequence=BinaryPrimitives.ReadInt32LittleEndian(header[12..]),raw=BinaryPrimitives.ReadInt32LittleEndian(header[16..]),packed=BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
                if((flags&~RequiredRecord)!=0 || header[9]!=0 || header[10]!=0 || header[11]!=0 || (byte)codec>2)
                    throw new InvalidDataException("Unsupported compressed record flags or codec.");
                bool known=tag<=FeaturesRecord;
                if(known && (version!=1 || flags!=RequiredRecord) || !known && (flags&RequiredRecord)!=0)
                    throw new InvalidDataException("Unsupported required save section or schema.");
                if(raw<0 || raw>_raw.Length || packed<0 || packed>_packed.Length || sequence<0 || raw==0 && tag!=EndRecord)
                    throw new InvalidDataException("Invalid compressed record length/sequence.");
                _decoded+=raw;_encoded+=RecordHeaderBytes+packed;
                if(_decoded>MaximumFileSize || _encoded>MaximumContainerSize)throw new InvalidDataException("Compressed save exceeds its decoded/encoded size limits.");
                hashed.ReadExactly(_packed.AsSpan(0,packed));_compression.Decompress(codec,_packed.AsSpan(0,packed),_raw.AsSpan(0,raw));
                Span<byte> checksum=stackalloc byte[32];SHA256.HashData(_raw.AsSpan(0,raw),checksum);
                if(!CryptographicOperations.FixedTimeEquals(checksum,header[24..]))throw new InvalidDataException("Save section checksum failed.");
                if(!known)continue; // Explicitly optional/ignorable extensions; required unknown data is rejected above.
                int chunkCount=metadata.StoredBlockCount/Chunk.BlockCount;
                if(tag==EndRecord)
                {
                    if(_phase!=2 || _chunks!=chunkCount || _states==0 || metadata.Version>=12 && Environment==null || metadata.Version>=14 && _featureFrames==0 || raw!=0 || packed!=0 || sequence!=0 || codec!=SaveCodec.Stored)
                        throw new InvalidDataException("Save is missing required sections or has an invalid end record.");
                    Span<byte> expected=stackalloc byte[32];source.ReadExactly(expected);
                    if(!CryptographicOperations.FixedTimeEquals(expected,whole.GetHashAndReset()))throw new InvalidDataException("Compressed save checksum failed.");
                    if(source.ReadByte()!=-1)throw new InvalidDataException("Compressed save has trailing data.");
                    _ended=true;_position=_length=0;return;
                }
                if(tag==FeaturesRecord)
                {
                    if(metadata.Version<14 || Environment==null || sequence!=_featureFrames++ || _features.Length+raw>16*1024*1024)throw new InvalidDataException("Invalid world feature frames.");
                    _features.Write(_raw,0,raw);continue;
                }
                if(tag==CoreHeaderRecord)
                {
                    if(_phase!=0 || sequence!=0 || !expectedHeader.AsSpan().SequenceEqual(_raw.AsSpan(0,raw)))throw new InvalidDataException("Mismatched/repeated core metadata record.");
                    _phase=1;
                }
                else if(tag==EnvironmentRecord)
                {
                    bool modern=metadata.Version>=13;
                    if(metadata.Version<12 || _phase!=2 || Environment!=null || raw!=(modern?12:10) || sequence!=0 || _raw[8]>(modern?1:2) || _raw[9]!=1
                        || modern && (_raw[10]>1 || _raw[11]>3))
                        throw new InvalidDataException("Invalid world environment section.");
                    double seconds=BinaryPrimitives.ReadDoubleLittleEndian(_raw);
                    if(!double.IsFinite(seconds) || seconds<0 || seconds>=WorldTime.DaySeconds)throw new InvalidDataException("Invalid world time.");
                    // Explicit migration: every pre-rule world starts with enabled
                    // cheats, Normal and Survival, even if v12 recorded Creative.
                    Environment=new(seconds,modern?(MDSM.Core.GameMode)_raw[8]:MDSM.Core.GameMode.Survival,
                        !modern || _raw[10]==1,modern?(MDSM.Core.Difficulty)_raw[11]:MDSM.Core.Difficulty.Normal);continue;
                }
                else if(tag==ChunkGroupRecord)
                {
                    int count=Math.Min(ChunksPerGroup,chunkCount-_chunks);
                    if(_phase!=1 || count<=0 || sequence!=_chunks || raw!=count*Chunk.BlockCount)throw new InvalidDataException("Missing, repeated or unordered chunk group.");
                    _chunks+=count;
                }
                else
                {
                    if(_phase==0 || _chunks!=chunkCount || sequence!=_states || Environment!=null)throw new InvalidDataException("Missing or unordered game-state section.");
                    _phase=2;_states++;
                }
                _position=0;_length=raw;return;
            }
        }
        protected override void Dispose(bool disposing){if(disposing){_compression.Dispose();_features.Dispose();}base.Dispose(disposing);}
    }
    private readonly record struct WorldEnvironment(double Seconds,MDSM.Core.GameMode Mode,bool AllowCheats,MDSM.Core.Difficulty Difficulty);
}
