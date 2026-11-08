using System;
using Microsoft.Xna.Framework;

namespace MDSM.Gameplay;

// Presentation only: never changes the camera, targeting, tool timing or physics.
public sealed class ViewModelSwing
{
    public const float Duration=.32f;
    private float _remaining;
    public float Progress=>_remaining<=0?0:1-_remaining/Duration;
    public void Trigger(){if(_remaining<=0)_remaining=Duration;}
    public void Update(float elapsed,bool mining)
    {
        if(!float.IsFinite(elapsed)||elapsed<=0)return;
        _remaining=Math.Max(0,_remaining-elapsed);
        if(mining && _remaining<=0)Trigger();
    }
    public void Reset()=>_remaining=0;
    public Matrix Transform
    {
        get
        {
            float p=Progress,s=MathF.Sin(p*MathF.PI),arc=MathF.Sin(MathF.Sqrt(p)*MathF.PI);
            return Matrix.CreateRotationX(-s*.32f)*Matrix.CreateRotationZ(arc*.22f)
                *Matrix.CreateTranslation(-arc*.16f,-s*.12f,-s*.15f);
        }
    }
}
