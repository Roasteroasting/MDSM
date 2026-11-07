using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Audio;

public readonly record struct SoundEvent(int Group, Vector3 Position, bool Positional, float Gain=1, float Pitch=0);

// Main-thread, bounded transient requests, never part of a world save.
public sealed class SoundEvents
{
    public const int Capacity=256;
    private readonly SoundEvent[] _queue=new SoundEvent[Capacity];
    private int _head,_count;
    public int Count=>_count;
    public int Discarded { get; private set; }
    public Vector3 Listener { get; private set; }
    private bool _hasListener;
    public void SetListener(Vector3 position){Listener=position;_hasListener=true;}
    public bool Nearby(Vector3 position,float range)=>!_hasListener || Vector3.DistanceSquared(position,Listener)<=range*range;
    public void Local(SoundKind kind,float gain=1)=>Enqueue(new(SoundCatalog.Find(kind),default,false,gain));
    public void At(SoundKind kind,Vector3 position,float gain=1)=>Enqueue(new(SoundCatalog.Find(kind),position,true,gain));
    public void Block(SoundKind kind,BlockType block,Vector3 position,float gain=1)
        =>Enqueue(new(SoundCatalog.Find(kind,BlockSounds.Material(block)),position,true,gain));
    public void Mob(SoundKind kind,PassiveMob mob)=>Enqueue(new(SoundCatalog.Find(kind,species:(int)mob.Species),mob.Position,true,1,mob.Baby?.18f:0));
    public void Enqueue(SoundEvent sound)
    {
        if((uint)sound.Group>=SoundCatalog.Groups.Length || !float.IsFinite(sound.Gain) || sound.Gain<=0
            || !float.IsFinite(sound.Position.LengthSquared()) || !float.IsFinite(sound.Pitch))return;
        var group=SoundCatalog.Groups[sound.Group];
        if(sound.Positional && !Nearby(sound.Position,group.Range))return;
        if(_count==Capacity)
        {
            Discarded++;
            if(group.Priority<3)return;
            _head=(_head+1)%Capacity;_count--;
        }
        _queue[(_head+_count)%Capacity]=sound;_count++;
    }
    public bool TryDequeue(out SoundEvent sound)
    {
        if(_count==0){sound=default;return false;}
        sound=_queue[_head];_head=(_head+1)%Capacity;_count--;return true;
    }
    public void Clear(){_head=_count=0;}
}
