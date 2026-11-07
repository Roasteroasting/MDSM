using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ZstdSharp;

if(args.Length<2)throw new ArgumentException("RAW_WORLD_FILE zstd|brotli [groupMiB=1] [level=3]");
string input=Path.GetFullPath(args[0]),codec=args[1];int group=args.Length>2?int.Parse(args[2]):1,level=args.Length>3?int.Parse(args[3]):3;
string output=input+"."+codec+"-"+group+"-"+level+".bench";
byte[] raw=new byte[group*1024*1024],packed=new byte[raw.Length+raw.Length/128+4096],decoded=new byte[raw.Length];
// Warm JIT/codec setup; every measured run then processes the entire representative file.
using var compressor=codec=="zstd"?new Compressor(level):null;using var decompressor=codec=="zstd"?new Decompressor():null;
int Encode(int length)=>codec=="zstd"?compressor.Wrap(raw.AsSpan(0,length),packed)
    :BrotliEncoder.TryCompress(raw.AsSpan(0,length),packed,out int written,level,22)?written:throw new Exception("Compression buffer too small.");
int Decode(int length,int expected)=>codec=="zstd"?decompressor.Unwrap(packed.AsSpan(0,length),decoded)
    :BrotliDecoder.TryDecompress(packed.AsSpan(0,length),decoded,out int written)?written:throw new Exception("Invalid Brotli frame.");
Encode(raw.Length);GC.Collect();long baseline=Process.GetCurrentProcess().WorkingSet64,allocated=GC.GetTotalAllocatedBytes(true);
var clock=Stopwatch.StartNew();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long original=0;
using(var source=File.OpenRead(input))using(var target=File.Create(output))using(var writer=new BinaryWriter(target))
{
    while(true)
    {
        int length=0,n;while(length<raw.Length && (n=source.Read(raw,length,raw.Length-length))>0)length+=n;if(length==0)break;
        hash.AppendData(raw.AsSpan(0,length));original+=length;int stored=Encode(length);writer.Write(length);writer.Write(stored);writer.Write(packed,0,stored);
    }
    target.Flush(true);
}
double save=clock.Elapsed.TotalSeconds;byte[] expectedHash=hash.GetHashAndReset();clock.Restart();
using(var source=File.OpenRead(output))using(var reader=new BinaryReader(source))
{
    while(source.Position<source.Length)
    {int expected=reader.ReadInt32(),length=reader.ReadInt32();source.ReadExactly(packed.AsSpan(0,length));if(Decode(length,expected)!=expected)throw new Exception("Decoded length mismatch.");hash.AppendData(decoded.AsSpan(0,expected));}
}
double load=clock.Elapsed.TotalSeconds;if(!hash.GetHashAndReset().AsSpan().SequenceEqual(expectedHash))throw new Exception("Lossy codec!");
var process=Process.GetCurrentProcess();long size=new FileInfo(output).Length;
Console.WriteLine($"{Path.GetFileName(input)} {codec} level={level} groupMiB={group}: rawMiB={original/1048576d:F3} compressedMiB={size/1048576d:F3} saveSeconds={save:F3} loadSeconds={load:F3} peakWorkingSetMiB={process.PeakWorkingSet64/1048576d:F1} baselineMiB={baseline/1048576d:F1} managedAllocatedMiB={(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d:F2} SHA256=PASS");
