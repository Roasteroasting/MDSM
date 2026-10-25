using Microsoft.Xna.Framework;

namespace MDSM;

public sealed record WorldSave(World World, string Name, Vector3 PlayerPosition, float Yaw, float Pitch);

public sealed record WorldMetadata(string Name, int Seed, Vector3 PlayerPosition, float Yaw, float Pitch,
    WorldSize Size = WorldSize.Normal);

// A session has no GPU resources. The renderer is replaced/disposed by Game1.
public sealed class GameSession
{
    public string Id { get; }
    public string Name { get; }
    public World World { get; }
    public Player Player { get; }
    public Camera Camera { get; }

    public GameSession(string id, WorldSave save)
    {
        Id = id;
        Name = save.Name;
        World = save.World;
        Player = new Player(save.PlayerPosition);
        Camera = new Camera { Position = Player.EyePosition };
        Camera.RestoreOrientation(save.Yaw, save.Pitch);
    }

    public WorldSave Capture() => new(World, Name, Player.Position, Camera.Yaw, Camera.Pitch);
}
