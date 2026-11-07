using MDSM.Audio;

bool verify=args.Contains("--verify");
string directory=args.FirstOrDefault(a=>a!="--verify");
string root=Path.GetFullPath(directory??Path.Combine(AppContext.BaseDirectory,"../../../../../"));
if(!File.Exists(Path.Combine(root,"MDSM.csproj")))throw new ArgumentException("Pass the MDSM project directory.");
string sounds=Path.Combine(root,"Content","Sounds");int count=0;long bytes=0;
foreach(var group in SoundCatalog.Groups)for(int variation=0;variation<group.Variations;variation++)
{
    string path=Path.Combine(root,"Content",group.Asset(variation));
    byte[] wav=Synthesizer.Generate(group,variation);
    if(verify){if(!File.Exists(path)||!File.ReadAllBytes(path).AsSpan().SequenceEqual(wav))throw new InvalidDataException("Non-reproducible asset: "+path);}
    else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,wav);}count++;bytes+=wav.Length;
}
// Only the marked, generated copy section belongs to this development tool.
string mgcb=Path.Combine(root,"Content","Content.mgcb"),text=File.ReadAllText(mgcb);
const string marker="# Generated procedural sounds - SoundGenerator";
const string endMarker="# End generated procedural sounds";
string suffix="";int start=text.IndexOf(marker,StringComparison.Ordinal);
if(start>=0)
{
    int end=text.IndexOf(endMarker,start,StringComparison.Ordinal);
    if(end>=0){end=text.IndexOf('\n',end);if(end<0)end=text.Length;else end++;}
    else
    {
        // Upgrade the original contiguous copy list without touching content that
        // may have subsequently been added below it.
        end=text.IndexOf('\n',start)+1;
        while(end<text.Length)
        {
            int lineEnd=text.IndexOf('\n',end);if(lineEnd<0)lineEnd=text.Length;
            string line=text[end..lineEnd].Trim();if(line.Length>0 && !line.StartsWith("/copy:Sounds/",StringComparison.Ordinal))break;
            end=lineEnd<text.Length?lineEnd+1:lineEnd;
        }
    }
    suffix=text[end..];text=text[..start];
}
var section=new System.Text.StringBuilder(marker+"\n");
foreach(var group in SoundCatalog.Groups)for(int v=0;v<group.Variations;v++)section.Append("/copy:").Append(group.Asset(v)).Append('\n');
section.Append(endMarker).Append('\n');
if(!verify)File.WriteAllText(mgcb,text.TrimEnd()+"\n\n"+section+suffix);
Console.WriteLine($"{(verify?"Verified":"Generated")} {count} deterministic mono PCM16 WAVs, {bytes:N0} bytes, under {sounds}.");
