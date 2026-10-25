namespace MDSM;

public enum GameState
{
    MainMenu,
    WorldCreation,
    WorldSelection,
    Playing,
    Paused
}

internal enum MenuAction
{
    None,
    NewWorld,
    SelectWorld,
    CreateWorld,
    LoadWorld,
    Back,
    Resume,
    Save,
    SaveAndReturn,
    Exit
}
