using System;
using Microsoft.Xna.Framework;

namespace MDSM.Gameplay;

// Presentation only. Logical camera position/direction and every ray use the unmodified pose.
public sealed class ViewBobbing
{
    internal const float DistancePerCycle=3f, HorizontalAmplitude=.06f, VerticalAmplitude=.075f;
    private Vector3 _previous;
    private bool _initialized;
    private float _phase,_strength;
    public Vector2 Offset { get; private set; }
    public void Update(Player player,bool enabled,float elapsed)=>UpdatePose(player.Position,player.Grounded,enabled,elapsed);
    internal void UpdatePose(Vector3 position,bool grounded,bool enabled,float elapsed)
    {
        if(!_initialized){_previous=position;_initialized=true;}
        float distance=new Vector2(position.X-_previous.X,position.Z-_previous.Z).Length();_previous=position;
        if(!enabled){_strength=0;Offset=Vector2.Zero;return;}
        if(!float.IsFinite(elapsed) || elapsed<=0)return;
        float target=grounded && distance>1e-5f?Math.Clamp(distance/elapsed/Player.MoveSpeed,0,1):0;
        _strength+=(target-_strength)*(1-MathF.Exp(-(target>_strength?6:8)*elapsed));
        if(target==0 && _strength<.0001f)_strength=0;
        // One smooth cycle per three traveled blocks (~1.43 Hz while walking).
        // Freeze phase in the air/at rest so the return to center cannot keep oscillating.
        if(target>0)_phase=(_phase+distance*(MathHelper.TwoPi/DistancePerCycle))%MathHelper.TwoPi;
        Offset=new Vector2(MathF.Sin(_phase)*HorizontalAmplitude,-MathF.Cos(_phase)*VerticalAmplitude)*_strength;
    }
    public void Reset(){_initialized=false;_strength=0;Offset=Vector2.Zero;}
}
