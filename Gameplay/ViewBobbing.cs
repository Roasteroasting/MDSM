using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Gameplay;

// Presentation only: bounded walking phase and fixed-step amplitude history.
public sealed class ViewBobbing
{
    // Classic walked-distance advances by 0.6 per block, then uses phase * PI.
    internal const float DistancePerCycle=10f/3f, HorizontalAmplitude=.05f, VerticalAmplitude=.10f;
    private Vector3 _previous;
    private bool _initialized;
    private float _phase,_advance,_strength,_previousStrength;
    internal void Reframe(Vector3 delta){if(_initialized)_previous+=delta;}
    public Vector2 Offset => Translation(_phase,_strength);
    public void Update(Player player,bool enabled,float elapsed,World world=null)
        =>UpdatePose(player.Position,player.Grounded&&!player.Creative&&(world==null||!Water.Intersects(world,player.Bounds)),enabled,elapsed);
    internal void UpdatePose(Vector3 position,bool grounded,bool enabled,float elapsed)
    {
        if(!_initialized){_previous=position;_initialized=true;}
        float distance=new Vector2(position.X-_previous.X,position.Z-_previous.Z).Length();_previous=position;
        if(!enabled){_strength=_previousStrength=_advance=0;return;}
        if(!float.IsFinite(elapsed)||elapsed<=0)return;
        _previousStrength=_strength;
        float target=grounded&&distance>1e-5f?Math.Clamp(distance/elapsed/Player.MoveSpeed,0,1):0;
        _strength+=(target-_strength)*(1-MathF.Exp(-(target>_strength?6:8)*elapsed));
        if(target==0&&_strength<.0001f)_strength=0;
        _advance=target>0?distance*(MathHelper.TwoPi/DistancePerCycle):0;
        _phase=(_phase+_advance)%MathHelper.TwoPi;
    }
    private static Vector2 Translation(float phase,float strength)
        =>new(MathF.Sin(phase)*HorizontalAmplitude*strength,-MathF.Abs(MathF.Cos(phase))*VerticalAmplitude*strength);
    internal Matrix Transform(float interpolation)
    {
        float alpha=Math.Clamp(interpolation,0,1);
        float phase=_phase-_advance*(1-alpha),amplitude=VerticalAmplitude*MathHelper.Lerp(_previousStrength,_strength,alpha);
        Vector2 translation=Translation(phase,MathHelper.Lerp(_previousStrength,_strength,alpha));
        // Transpose the classic column-vector T * Rz * Rx ordering for
        // MonoGame row vectors. This is applied AFTER the ordinary view matrix,
        // so sway/dip are camera-local, with no forward translation or orbit.
        return Matrix.CreateRotationX(MathHelper.ToRadians(MathF.Abs(MathF.Cos(phase-.2f))*amplitude*5))
            *Matrix.CreateRotationZ(MathHelper.ToRadians(MathF.Sin(phase)*amplitude*3))
            *Matrix.CreateTranslation(translation.X,translation.Y,0);
    }
    public void Reset(){_initialized=false;_phase=_advance=_strength=_previousStrength=0;}
}
