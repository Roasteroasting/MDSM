using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public sealed class Camera
{
    private const float MouseSensitivity = 0.0025f;
    private float _yaw;
    private float _pitch = -0.3f;

    public Vector3 Position { get; set; }
    public Vector3 Forward => new(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), -MathF.Cos(_yaw) * MathF.Cos(_pitch));
    public Vector3 HorizontalForward => new(MathF.Sin(_yaw), 0, -MathF.Cos(_yaw));
    public Vector3 Right => new(MathF.Cos(_yaw), 0, MathF.Sin(_yaw));
    public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.Up);

    public Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(75), aspectRatio, 0.05f, 100f);

    public void Look(int deltaX, int deltaY)
    {
        _yaw = MathHelper.WrapAngle(_yaw + deltaX * MouseSensitivity);
        _pitch = MathHelper.Clamp(_pitch - deltaY * MouseSensitivity, -MathHelper.PiOver2 + 0.01f, MathHelper.PiOver2 - 0.01f);
    }
}
