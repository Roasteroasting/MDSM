using Microsoft.Xna.Framework;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Gameplay;

// Global authoritative positions are doubles. Existing movement and model code
// works in a bounded floating-origin frame; absolute coordinates are never
// narrowed before subtraction. Finite worlds retain their original float path.
internal struct FramedPosition
{
    private World _world;
    private Vector3 _local;
    private WorldPosition _absolute;
    internal Vector3 Local
    {
        readonly get=>_world==null?_local:_world.LocalPosition(_absolute);
        set{_local=value;if(_world!=null)_absolute=_world.AbsolutePosition(value);}
    }
    internal readonly WorldPosition Absolute=>_world==null?new(_local.X,_local.Y,_local.Z):_absolute;
    internal void Bind(World world)
    {if(!world.IsInfinite || _world==world)return;_absolute=world.AbsolutePosition(_local);_world=world;}
    internal void SetAbsolute(WorldPosition value){_absolute=value;if(_world==null)_local=new((float)value.X,(float)value.Y,(float)value.Z);}
}
