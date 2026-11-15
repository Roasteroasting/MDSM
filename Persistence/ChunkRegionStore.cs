using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MDSM.Worlds.Streaming;

namespace MDSM.Persistence;

// Independently compressed opaque chunk snapshots. The caller owns the versioned
// block/entity/timer payload. Two checksummed indexes commit only after data has
// been flushed; an interrupted newest index falls back to the previous one.
internal sealed class ChunkRegionStore : IDisposable
{
    internal const int MaximumPayload=1024*1024;
    internal const int MaximumWriteBatch=64;
    private const uint Magic=0x524D444D;
    private const int Slots=1024,Header=32,Page=8+Slots*12+32,DataStart=Header+Page*2,RecordHeader=64;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate=new(1,1);
    // The gate owns these reusable codec contexts/buffer, including compaction.
    private readonly SaveCompression _codec=new();
    private readonly byte[] _encoded=new byte[SaveCompression.PackedLimit];
    internal ChunkRegionStore(string directory){_directory=Path.GetFullPath(directory);}
    private string PathFor(ChunkAddress region)=>Path.Combine(_directory,FormattableString.Invariant($"r.{region.X}.{region.Z}.mdr"));
    internal async Task<byte[]> ReadAsync(ChunkAddress address,CancellationToken token=default)
    {
        Coordinates.Validate(address);await _gate.WaitAsync(token).ConfigureAwait(false);
        try{return await Task.Run(()=>Read(address),token).ConfigureAwait(false);}finally{_gate.Release();}
    }
    private byte[] Read(ChunkAddress address)
    {
        string path=PathFor(address.Region);if(!File.Exists(path))return null;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        ValidateHeader(file,address.Region);var index=ReadIndex(file);int slot=address.RegionSlot;
        long offset=BinaryPrimitives.ReadInt64LittleEndian(index.Bytes.AsSpan(8+slot*12));
        int length=BinaryPrimitives.ReadInt32LittleEndian(index.Bytes.AsSpan(16+slot*12));
        if(offset==0 && length==0)return null;
        if(offset<DataStart || length<RecordHeader || length>SaveCompression.PackedLimit+RecordHeader || offset>file.Length-length)throw new InvalidDataException("Invalid region record bounds.");
        file.Position=offset;byte[] header=new byte[RecordHeader];file.ReadExactly(header);
        if(BinaryPrimitives.ReadInt32LittleEndian(header)!=1 || BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(4))!=address.X
            ||BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12))!=address.Z || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20))!=(int)SaveCodec.Zstandard)throw new InvalidDataException("Region record identity/schema mismatch.");
        int raw=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24)),packed=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28));
        if(raw<1||raw>MaximumPayload||packed<1||packed!=length-RecordHeader)throw new InvalidDataException("Invalid compressed chunk length.");
        byte[] decoded=new byte[raw];file.ReadExactly(_encoded.AsSpan(0,packed));
        _codec.Decompress(SaveCodec.Zstandard,_encoded.AsSpan(0,packed),decoded);
        if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(decoded),header.AsSpan(32,32)))throw new InvalidDataException("Chunk checksum mismatch.");
        return decoded;
    }
    internal async Task WriteAsync(ChunkAddress address,ReadOnlyMemory<byte> payload,CancellationToken token=default)
    {
        Coordinates.Validate(address);if(payload.Length<1||payload.Length>MaximumPayload)throw new ArgumentException("Chunk payload exceeds its budget.");
        // Snapshot before dispatch: the worker never observes mutable simulation data.
        byte[] snapshot=payload.ToArray();await _gate.WaitAsync(token).ConfigureAwait(false);
        try{await Task.Run(()=>{Write(address,snapshot);Compact(address.Region,false);},token).ConfigureAwait(false);}finally{_gate.Release();}
    }
    internal async Task WriteBatchAsync((ChunkAddress Address,byte[] Payload)[] records)
    {
        if(records.Length<1||records.Length>MaximumWriteBatch)throw new ArgumentException("Region write batch exceeds its bound.");
        foreach(var record in records){Coordinates.Validate(record.Address);if(record.Payload.Length is <1 or >MaximumPayload)throw new ArgumentException("Chunk payload exceeds its budget.");}
        await _gate.WaitAsync().ConfigureAwait(false);
        try{await Task.Run(()=>{foreach(var group in records.GroupBy(r=>r.Address.Region)){WriteRecords(group.Key,group.ToArray());Compact(group.Key,false);}}).ConfigureAwait(false);}
        finally{_gate.Release();}
    }
    internal async Task CompactAsync(ChunkAddress region,CancellationToken token=default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try{await Task.Run(()=>Compact(region,true),token).ConfigureAwait(false);}finally{_gate.Release();}
    }
    private void Compact(ChunkAddress region,bool force)
    {
        string path=PathFor(region);if(!File.Exists(path))return;
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".compact";
        try
        {
            using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                ValidateHeader(source,region);var current=ReadIndex(source);long live=0;
                for(int slot=0;slot<Slots;slot++)live+=BinaryPrimitives.ReadInt32LittleEndian(current.Bytes.AsSpan(16+slot*12));
                if(!force && source.Length<=Math.Max(8L*1024*1024,DataStart+live*4))return;
                using var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
                byte[] header=new byte[Header];source.Position=0;source.ReadExactly(header);target.Write(header);target.SetLength(DataStart);target.Position=DataStart;
                byte[] index=new byte[Page];BinaryPrimitives.WriteInt64LittleEndian(index,checked(BinaryPrimitives.ReadInt64LittleEndian(current.Bytes)+1));
                for(int slot=0;slot<Slots;slot++)
                {
                    int entry=8+slot*12;long offset=BinaryPrimitives.ReadInt64LittleEndian(current.Bytes.AsSpan(entry));int length=BinaryPrimitives.ReadInt32LittleEndian(current.Bytes.AsSpan(entry+8));
                    if(offset==0 && length==0)continue;
                    if(offset<DataStart || length<RecordHeader || length>SaveCompression.PackedLimit+RecordHeader || offset>source.Length-length)throw new InvalidDataException("Invalid compaction record bounds.");
                    var address=new ChunkAddress(checked(region.X*32+slot%32),checked(region.Z*32+slot/32));
                    _=Read(address); // Validate identity, decoded budget and checksum before replacement.
                    source.Position=offset;byte[] record=new byte[length];source.ReadExactly(record);
                    BinaryPrimitives.WriteInt64LittleEndian(index.AsSpan(entry),target.Position);BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(entry+8),length);target.Write(record);
                }
                Seal(index);target.Position=Header;target.Write(index);target.Write(index);target.Flush(true);
            }
            File.Replace(temporary,path,null);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void Write(ChunkAddress address,byte[] payload)
        =>WriteRecords(address.Region,new[]{(address,payload)});
    private void WriteRecords(ChunkAddress region,(ChunkAddress Address,byte[] Payload)[] records)
    {
        Directory.CreateDirectory(_directory);string path=PathFor(region);
        if(!File.Exists(path))Initialize(path,region);
        using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);
        ValidateHeader(file,region);var current=ReadIndex(file);byte[] next=current.Bytes;
        Span<byte> header=stackalloc byte[RecordHeader];
        foreach(var (address,payload) in records)
        {
        int packed=_codec.Compress(SaveCodec.Zstandard,payload,_encoded);header.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(header,1);BinaryPrimitives.WriteInt64LittleEndian(header[4..],address.X);BinaryPrimitives.WriteInt64LittleEndian(header[12..],address.Z);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..],(int)SaveCodec.Zstandard);BinaryPrimitives.WriteInt32LittleEndian(header[24..],payload.Length);BinaryPrimitives.WriteInt32LittleEndian(header[28..],packed);SHA256.HashData(payload).CopyTo(header[32..]);
        long position=file.Length;file.Position=position;file.Write(header);file.Write(_encoded.AsSpan(0,packed));
        int entry=8+address.RegionSlot*12;BinaryPrimitives.WriteInt64LittleEndian(next.AsSpan(entry),position);BinaryPrimitives.WriteInt32LittleEndian(next.AsSpan(entry+8),packed+RecordHeader);
        }
        file.Flush(true);long generation=checked(BinaryPrimitives.ReadInt64LittleEndian(next)+1);BinaryPrimitives.WriteInt64LittleEndian(next,generation);Seal(next);
        file.Position=Header+(1-current.PageNumber)*Page;file.Write(next);file.Flush(true);
    }
    private static void Initialize(string path,ChunkAddress region)
    {
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                byte[] header=new byte[Header];BinaryPrimitives.WriteUInt32LittleEndian(header,Magic);BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),1);
                BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8),region.X);BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16),region.Z);file.Write(header);
                byte[] empty=new byte[Page];Seal(empty);file.Write(empty);file.Write(empty);file.Flush(true);
            }
            File.Move(temporary,path);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void ValidateHeader(FileStream file,ChunkAddress region)
    {
        if(file.Length<DataStart)throw new InvalidDataException("Truncated region header/index.");
        Span<byte> bytes=stackalloc byte[Header];file.Position=0;file.ReadExactly(bytes);
        if(BinaryPrimitives.ReadUInt32LittleEndian(bytes)!=Magic||BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])!=1
            ||BinaryPrimitives.ReadInt64LittleEndian(bytes[8..])!=region.X||BinaryPrimitives.ReadInt64LittleEndian(bytes[16..])!=region.Z)throw new InvalidDataException("Unsupported/mismatched region header.");
    }
    private static void Seal(byte[] bytes)=>SHA256.HashData(bytes.AsSpan(0,Page-32)).CopyTo(bytes.AsSpan(Page-32));
    private static (byte[] Bytes,int PageNumber) ReadIndex(FileStream file)
    {
        byte[] best=null;int number=-1;long generation=-1;
        for(int i=0;i<2;i++)
        {
            byte[] candidate=new byte[Page];file.Position=Header+i*Page;file.ReadExactly(candidate);
            long g=BinaryPrimitives.ReadInt64LittleEndian(candidate);
            if(g<0 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(candidate.AsSpan(0,Page-32)),candidate.AsSpan(Page-32)))continue;
            if(g>generation){best=candidate;number=i;generation=g;}
        }
        if(best==null)throw new InvalidDataException("Both region indexes are corrupt.");return(best,number);
    }
    public void Dispose(){_codec.Dispose();_gate.Dispose();}
}
