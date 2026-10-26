using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public readonly record struct FallingBlockState(BlockType Block, int X, int Z, double Y, double DownwardSpeed);

// Feet position: the cube occupies [X,X+1] x [Y,Y+1] x [Z,Z+1].
public sealed class FallingBlock
{
    public BlockType Block { get; }
    public int X { get; }
    public int Z { get; }
    public double Y { get; internal set; }
    public double DownwardSpeed { get; internal set; }
    internal double PreviousY;
    internal int Index;
    internal FallingBlock(FallingBlockState state)
    { Block = state.Block; X = state.X; Z = state.Z; Y = PreviousY = state.Y; DownwardSpeed = state.DownwardSpeed; }
    public Vector3 RenderPosition(float fraction) => new(X, (float)(PreviousY + (Y - PreviousY) * Math.Clamp(fraction, 0, 1)), Z);
    public BoundingBox Bounds => new(new Vector3(X, (float)Y, Z), new Vector3(X + 1, (float)Y + 1, Z + 1));
    public FallingBlockState Capture() => new(Block, X, Z, Y, DownwardSpeed);
}
