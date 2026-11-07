using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace MDSM.Persistence;

internal enum SaveCodec : byte { Stored=0, Brotli=1, Zstandard=2 }

// One codec context and two bounded buffers per operation, never a world-sized copy.
internal sealed class SaveCompression : IDisposable
{
    internal const int FrameBytes=1024*1024;
    internal const int PackedLimit=FrameBytes+FrameBytes/128+4096;
    private Compressor _compressor;
    private Decompressor _decompressor;
    public int Compress(SaveCodec codec,ReadOnlySpan<byte> input,Span<byte> output)
    {
        try
        {
            if(codec==SaveCodec.Zstandard)return (_compressor??=new Compressor(3)).Wrap(input,output);
            if(codec==SaveCodec.Brotli && BrotliEncoder.TryCompress(input,output,out int written,3,22))return written;
            if(codec==SaveCodec.Stored){input.CopyTo(output);return input.Length;}
            throw new IOException("Unsupported world compression codec.");
        }
        catch(ZstdException e){throw new IOException("World compression failed.",e);}
    }
    public void Decompress(SaveCodec codec,ReadOnlySpan<byte> input,Span<byte> output)
    {
        try
        {
            if(codec==SaveCodec.Stored)
            {if(input.Length!=output.Length)throw new InvalidDataException("Stored record length mismatch.");input.CopyTo(output);return;}
            if(codec==SaveCodec.Zstandard)
            {
                if(_decompressor==null)
                {_decompressor=new Decompressor();_decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax,20);}
                if(_decompressor.Unwrap(input,output)!=output.Length)throw new InvalidDataException("Zstandard decoded length mismatch.");
                return;
            }
            if(codec==SaveCodec.Brotli)
            {
                using var decoder=new BrotliDecoder();
                if(decoder.Decompress(input,output,out int consumed,out int written)!=OperationStatus.Done || consumed!=input.Length || written!=output.Length)
                    throw new InvalidDataException("Invalid or truncated Brotli record.");
                return;
            }
            throw new InvalidDataException("Unsupported world compression codec.");
        }
        catch(ZstdException e){throw new InvalidDataException("Invalid or truncated Zstandard record.",e);}
    }
    public void Dispose(){_compressor?.Dispose();_decompressor?.Dispose();}
}
