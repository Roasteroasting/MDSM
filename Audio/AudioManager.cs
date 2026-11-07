using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using MDSM.Core;

namespace MDSM.Audio;

public sealed class SoundRateLimiter
{
    private readonly double[] _next=new double[SoundCatalog.Groups.Length];
    private double _time=1;
    public void Advance(double elapsed){if(double.IsFinite(elapsed)&&elapsed>0)_time+=elapsed;}
    public bool Accept(int group)
    {if(_time+1e-9<_next[group])return false;_next[group]=_time+SoundCatalog.Groups[group].Interval;return true;}
}

public static class SoundSpatial
{
    public static (float Volume,float Pan) Calculate(SoundEvent sound,GameSettings settings,Vector3 listener,Vector3 right)
    {
        var group=SoundCatalog.Groups[sound.Group];float gain=group.Gain*sound.Gain*settings.MasterVolume*settings.Volume(group.Category),pan=0;
        if(sound.Positional)
        {
            Vector3 delta=sound.Position-listener;float distance=delta.Length();
            if(distance>=group.Range)return(0,0);
            float fade=1-Math.Max(0,distance-2)/Math.Max(1,group.Range-2);gain*=fade*fade;
            if(distance>.001f)pan=Math.Clamp(Vector3.Dot(delta/distance,right),-1,1)*.85f;
        }
        return(Math.Clamp(gain,0,1),pan);
    }
}

// Owns WAV resources and pre-created instance pools. Only this class talks to the
// audio device; every gameplay request is a small value in a bounded queue.
public sealed class AudioManager : IDisposable
{
    public const int MaximumVoices=32, InstancesPerClip=2;
    private sealed class Clip
    {
        public SoundEffect Effect;
        public readonly SoundEffectInstance[] Instances=new SoundEffectInstance[InstancesPerClip];
    }
    private readonly record struct Voice(SoundEvent Event,SoundEffectInstance Instance);
    private readonly Clip[][] _clips=new Clip[SoundCatalog.Groups.Length][];
    private readonly Voice[] _active=new Voice[MaximumVoices];
    private readonly int[] _lastVariant=new int[SoundCatalog.Groups.Length];
    private readonly SoundRateLimiter _rates=new();
    private readonly Random _random=new(0x41554449);
    private readonly GameSettings _settings;
    private int _count;
    public int ActiveVoices=>_count;
    public int HighWaterVoices { get; private set; }
    public int LoadedClips { get; private set; }
    public int MissingClips { get; private set; }
    public int Played { get; private set; }
    public int Rejected { get; private set; }
    public bool Available { get; private set; }=true;
    public AudioManager(string contentRoot,GameSettings settings,Func<string,SoundEffect> loader=null)
    {
        _settings=settings;Array.Fill(_lastVariant,-1);
        string root=Path.IsPathFullyQualified(contentRoot)?contentRoot:Path.Combine(AppContext.BaseDirectory,contentRoot);
        loader??=path=>{using var file=File.OpenRead(path);return SoundEffect.FromStream(file);};
        for(int group=0;group<_clips.Length;group++)
        {
            _clips[group]=new Clip[SoundCatalog.Groups[group].Variations];
            for(int variation=0;variation<_clips[group].Length;variation++)
            {
                Clip clip=null;
                try
                {
                    clip=new(){Effect=loader(Path.Combine(root,SoundCatalog.Groups[group].Asset(variation)))};
                    for(int i=0;i<InstancesPerClip;i++)clip.Instances[i]=clip.Effect.CreateInstance();
                    _clips[group][variation]=clip;LoadedClips++;
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {DisposeClip(clip);MissingClips++;}
                catch(Exception e) when(e is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
                {
                    DisposeClip(clip);Available=false;Console.Error.WriteLine("AUDIO UNAVAILABLE: "+e.Message);Dispose();return;
                }
            }
        }
        if(MissingClips>0)Console.Error.WriteLine($"AUDIO: {MissingClips} missing/invalid clips; unavailable sounds will be skipped.");
    }
    public void Update(double elapsed,SoundEvents ui,SoundEvents world,Vector3 listener,Vector3 right,bool playing,bool foreground=true)
    {
        try { UpdateCore(elapsed,ui,world,listener,right,playing,foreground); }
        catch(Exception e) when(e is NoAudioHardwareException or InvalidOperationException)
        {Available=false;StopAll();ui?.Clear();world?.Clear();Console.Error.WriteLine("AUDIO DEVICE DISABLED: "+e.Message);}
    }
    private void UpdateCore(double elapsed,SoundEvents ui,SoundEvents world,Vector3 listener,Vector3 right,bool playing,bool foreground)
    {
        _rates.Advance(elapsed);
        for(int i=_count-1;i>=0;i--)
        {
            var voice=_active[i];var group=SoundCatalog.Groups[voice.Event.Group];
            var mix=SoundSpatial.Calculate(voice.Event,_settings,listener,right);
            if(!foreground || !playing && !group.InMenus || mix.Volume<=0 || voice.Instance.State==SoundState.Stopped)
            {voice.Instance.Stop();_active[i]=_active[--_count];_active[_count]=default;continue;}
            voice.Instance.Volume=mix.Volume;voice.Instance.Pan=mix.Pan;
        }
        Drain(ui);if(!ReferenceEquals(ui,world))Drain(world);
        void Drain(SoundEvents events)
        {
            if(events==null)return;
            while(events.TryDequeue(out var sound))
                if(foreground && (playing || SoundCatalog.Groups[sound.Group].InMenus))Play(sound,listener,right);
        }
    }
    public bool Play(SoundEvent sound,Vector3 listener,Vector3 right)
    {
        if(!Available || (uint)sound.Group>=_clips.Length){Rejected++;return false;}
        var mix=SoundSpatial.Calculate(sound,_settings,listener,right);
        if(mix.Volume<=.001f || !_rates.Accept(sound.Group)){Rejected++;return false;}
        var group=SoundCatalog.Groups[sound.Group];int variant=_random.Next(group.Variations);
        if(variant==_lastVariant[sound.Group] && group.Variations>1)variant=(variant+1)%group.Variations;
        Clip clip=_clips[sound.Group][variant];
        if(clip==null)foreach(var available in _clips[sound.Group])if(available!=null){clip=available;break;}
        if(clip==null){Rejected++;return false;}
        SoundEffectInstance instance=null;
        foreach(var candidate in clip.Instances)if(candidate.State==SoundState.Stopped)
        {
            for(int i=_count-1;i>=0;i--)if(ReferenceEquals(_active[i].Instance,candidate))
            {_active[i]=_active[--_count];_active[_count]=default;}
            instance=candidate;break;
        }
        if(instance==null){Rejected++;return false;}
        if(_count==MaximumVoices)
        {
            int weakest=0;for(int i=1;i<_count;i++)if(SoundCatalog.Groups[_active[i].Event.Group].Priority<SoundCatalog.Groups[_active[weakest].Event.Group].Priority)weakest=i;
            if(group.Priority<=SoundCatalog.Groups[_active[weakest].Event.Group].Priority && group.Kind!=SoundKind.PlayerDeath){Rejected++;return false;}
            _active[weakest].Instance.Stop();_active[weakest]=_active[--_count];_active[_count]=default;
        }
        try
        {
            instance.Volume=mix.Volume;instance.Pan=mix.Pan;instance.Pitch=Math.Clamp(sound.Pitch+(float)(_random.NextDouble()-.5)*.12f,-1,1);
            instance.Play();_active[_count++]=new(sound,instance);_lastVariant[sound.Group]=variant;
            Played++;HighWaterVoices=Math.Max(HighWaterVoices,_count);return true;
        }
        catch(Exception e) when(e is NoAudioHardwareException or InvalidOperationException)
        {Available=false;StopAll();Console.Error.WriteLine("AUDIO PLAYBACK DISABLED: "+e.Message);return false;}
    }
    public void StopAll()
    {for(int i=0;i<_count;i++){try{_active[i].Instance.Stop();}catch(Exception e) when(e is InvalidOperationException or NoAudioHardwareException){} _active[i]=default;}_count=0;}
    private static void DisposeClip(Clip clip)
    {if(clip==null)return;foreach(var instance in clip.Instances)instance?.Dispose();clip.Effect?.Dispose();}
    public void Dispose()
    {
        StopAll();foreach(var group in _clips)if(group!=null)for(int i=0;i<group.Length;i++){DisposeClip(group[i]);group[i]=null;}
        Available=false;
    }
}
