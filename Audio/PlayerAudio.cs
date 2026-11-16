using System;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Audio;

public sealed class PlayerAudio
{
    private bool _initialized,_water,_grounded;
    private Vector3 _feet;
    private float _distance,_swimDistance,_peak;
    private double _waterSoundCooldown;
    public void Reset(){_initialized=false;_waterSoundCooldown=0;}
    public void Update(Player player,World world,float elapsedSeconds=1f/120)
    {
        bool water=Water.Intersects(world,player.Bounds);
        if(float.IsFinite(elapsedSeconds)&&elapsedSeconds>0)_waterSoundCooldown=Math.Max(0,_waterSoundCooldown-elapsedSeconds);
        Vector3 delta=player.Position-_feet;
        if(!_initialized || delta.LengthSquared()>9){_initialized=true;_distance=_swimDistance=0;_peak=player.Position.Y;_waterSoundCooldown=0;Remember();return;}
        if(water!=_water)
        {
            // Surface strokes remain audible, but never produce a splash pair
            // on every crossing. This clock advances only with active gameplay.
            if(_waterSoundCooldown<=.00001){world.Sounds?.Local(water?SoundKind.WaterEnter:SoundKind.WaterExit,.35f);_waterSoundCooldown=1;}
            _distance=_swimDistance=0;
        }
        if(water)
        {
            _swimDistance+=delta.Length();
            if(_swimDistance>=.85f){_swimDistance%=.85f;world.Sounds?.Local(SoundKind.Swim);}
        }
        else if(!player.Flying && !player.IsDead)
        {
            _peak=Math.Max(_peak,player.Position.Y);
            if(player.Grounded && !_grounded && _peak-player.Position.Y>.15f)
            {
                world.Sounds?.Block(SoundKind.Land,BlockSounds.Surface(world,player.Position),player.Position);
                world.Particles.Burst(BlockSounds.Surface(world,player.Position),player.Position,true);
            }
            if(player.Grounded)
            {
                _peak=player.Position.Y;
                _distance+=new Vector2(delta.X,delta.Z).Length();
                if(_distance>=1.7f){_distance%=1.7f;world.Sounds?.Block(SoundKind.Step,BlockSounds.Surface(world,player.Position),player.Position,player.Crouching?.55f:1);}
            }
            else _distance=0;
        }
        Remember();
        void Remember(){_feet=player.Position;_water=water;_grounded=player.Grounded;}
    }
}
