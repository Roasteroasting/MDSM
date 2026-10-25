using Microsoft.Xna.Framework;

namespace MDSM;

public sealed record WorldSave(World World, string Name, Vector3 PlayerPosition, float Yaw, float Pitch,
    Inventory Inventory = null, DroppedItemState[] DroppedItems = null);

public sealed record WorldMetadata(string Name, int Seed, Vector3 PlayerPosition, float Yaw, float Pitch,
    WorldSize Size = WorldSize.Normal, int Version = 1);

// A session has no GPU resources. The renderer is replaced/disposed by Game1.
public sealed class GameSession
{
    public string Id { get; }
    public string Name { get; }
    public World World { get; }
    public Player Player { get; }
    public Camera Camera { get; }
    public Inventory Inventory { get; }
    public InventoryInteraction InventoryInteraction { get; }
    public DroppedItems Items { get; }
    public MiningController Mining { get; } = new();

    public GameSession(string id, WorldSave save)
    {
        Id = id;
        Name = save.Name;
        World = save.World;
        Inventory = save.Inventory?.Clone() ?? Inventory.CreateStarter();
        InventoryInteraction = new InventoryInteraction(Inventory);
        Items = new DroppedItems(World, save.DroppedItems);
        Player = new Player(save.PlayerPosition);
        Camera = new Camera { Position = Player.EyePosition };
        Camera.RestoreOrientation(save.Yaw, save.Pitch);
    }

    public WorldSave Capture()
    {
        if (!InventoryInteraction.Cursor.IsEmpty)
            throw new System.InvalidOperationException("Return cursor-held items before saving.");
        return new(World, Name, Player.Position, Camera.Yaw, Camera.Pitch, Inventory.Clone(), Items.Capture());
    }
}
