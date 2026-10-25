namespace MDSM;

public enum GameState
{
    MainMenu,
    WorldCreation,
    WorldSelection,
    WorldRename,
    WorldDeleteConfirmation,
    Playing,
    Inventory,
    Paused
}

internal enum MenuAction
{
    None,
    NewWorld,
    SelectWorld,
    CreateWorld,
    LoadWorld,
    RenameWorld,
    ConfirmRename,
    DeleteWorld,
    ConfirmDelete,
    OpenSavesFolder,
    Back,
    Resume,
    Save,
    SaveAndReturn,
    Exit
}
