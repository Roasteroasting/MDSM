using Microsoft.Xna.Framework.Input;

namespace MDSM.Input;

internal sealed class CreativeFlightInput
{
    internal bool FastFlight {get;private set;}
    internal bool Flying {get;private set;}=true;
    internal bool FlightChanged {get;private set;}
    internal const double DoubleTapSeconds=.3;
    private double _spaceAge=double.PositiveInfinity;
    internal void CancelSpaceTap()=>_spaceAge=double.PositiveInfinity;
    internal void Reset(){FastFlight=false;Flying=true;FlightChanged=false;CancelSpaceTap();}
    internal void Update(KeyboardState current,KeyboardState previous,bool creative,double elapsed=0)
    {
        if(!creative){Reset();return;}
        FlightChanged=false;
        _spaceAge+=double.IsFinite(elapsed)?System.Math.Max(0,elapsed):0;
        if(current.IsKeyDown(Keys.Space)&&previous.IsKeyUp(Keys.Space))
        {
            if(_spaceAge<=DoubleTapSeconds){Flying=!Flying;FlightChanged=true;CancelSpaceTap();}
            else _spaceAge=0;
        }
        if(current.IsKeyDown(Keys.LeftControl)&&previous.IsKeyUp(Keys.LeftControl)
            ||current.IsKeyDown(Keys.RightControl)&&previous.IsKeyUp(Keys.RightControl))
            FastFlight=!FastFlight;
    }
}
