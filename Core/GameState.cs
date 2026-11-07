namespace MDSM.Core;

public enum GameState
{
    MainMenu,
    WorldCreation,
    WorldSelection,
    WorldRename,
    WorldDeleteConfirmation,
    Playing,
    Inventory,
    Paused,
    Death,
    Settings,
    Controls,
    About
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
    Respawn,
    Save,
    SaveAndReturn,
    Exit,
    OpenSettings,
    SettingsChanged,
    OpenControls,
    OpenAbout
}
