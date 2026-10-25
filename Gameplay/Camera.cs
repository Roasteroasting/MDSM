using System;
using Microsoft.Xna.Framework;

namespace MDSM.Gameplay;

public sealed class Camera
{
    public const float FarPlane = 384f;
    private const float MouseSensitivity = 0.0025f;
    private float _yaw;
    private float _pitch = -0.3f;
    public const float PitchLimit = MathHelper.PiOver2 - 0.01f;
    public float Yaw => _yaw;
    public float Pitch => _pitch;

    public Vector3 Position { get; set; }
    public Vector3 Forward => new(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), -MathF.Cos(_yaw) * MathF.Cos(_pitch));
    public Vector3 HorizontalForward => new(MathF.Sin(_yaw), 0, -MathF.Cos(_yaw));
    public Vector3 Right => new(MathF.Cos(_yaw), 0, MathF.Sin(_yaw));
    public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.Up);

    public Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(75), aspectRatio, 0.05f, FarPlane);

    public void Look(int deltaX, int deltaY)
    {
        _yaw = MathHelper.WrapAngle(_yaw + deltaX * MouseSensitivity);
        _pitch = MathHelper.Clamp(_pitch - deltaY * MouseSensitivity, -PitchLimit, PitchLimit);
    }

    public void RestoreOrientation(float yaw, float pitch)
    {
        if (!float.IsFinite(yaw) || MathF.Abs(yaw) > MathHelper.Pi
            || !float.IsFinite(pitch) || MathF.Abs(pitch) > PitchLimit)
            throw new ArgumentOutOfRangeException(nameof(yaw), "Invalid camera orientation.");
        _yaw = yaw;
        _pitch = pitch;
    }
}
