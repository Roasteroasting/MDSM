using System;
using System.IO;
using System.Text.Json;

namespace MDSM.Core;

public enum Difficulty { Peaceful, Easy, Normal, Hard }
public enum GameMode { Survival, Creative }

public sealed class GameSettings
{
    public const int MinimumFps = 5, MaximumFps = 360, DefaultFps = 60;
    private int _fpsCap = DefaultFps;
    public int FpsCap { get => _fpsCap; set => _fpsCap = Math.Clamp(value, MinimumFps, MaximumFps); }
    public bool UncappedFps { get; set; }
    public bool ViewBobbing { get; set; } = true;
    private Difficulty _difficulty = Difficulty.Normal;
    private GameMode _gameMode;
    public Difficulty Difficulty { get => _difficulty; set => _difficulty = Enum.IsDefined(value) ? value : Difficulty.Normal; }
    public GameMode GameMode { get => _gameMode; set => _gameMode = Enum.IsDefined(value) ? value : GameMode.Survival; }
    public double DamageMultiplier => Difficulty is Difficulty.Peaceful or Difficulty.Easy ? .5 : Difficulty == Difficulty.Hard ? 1.5 : 1;
    public int? EffectiveFps => UncappedFps ? null : FpsCap;
}

public sealed class SettingsStore
{
    public string FilePath { get; }
    public string LoadError { get; private set; }
    public SettingsStore(string filePath) => FilePath = Path.GetFullPath(filePath);

    public GameSettings Load()
    {
        var settings = new GameSettings();
        LoadError = null;
        try
        {
            if (!File.Exists(FilePath)) return settings;
            using var file = File.OpenRead(FilePath);
            if (file.Length > 4096) throw new JsonException("Settings file is too large.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected settings object.");
            if (root.TryGetProperty("FpsCap", out var cap) && cap.TryGetInt32(out int value)) settings.FpsCap = value;
            if (root.TryGetProperty("UncappedFps", out var uncapped) && uncapped.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.UncappedFps = uncapped.GetBoolean();
            if(root.TryGetProperty("ViewBobbing",out var bobbing) && bobbing.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.ViewBobbing=bobbing.GetBoolean();
            if (root.TryGetProperty("Difficulty", out var difficulty) && difficulty.TryGetInt32(out int difficultyValue))
                settings.Difficulty = (Difficulty)difficultyValue;
            if (root.TryGetProperty("GameMode", out var mode) && mode.TryGetInt32(out int modeValue))
                settings.GameMode = (GameMode)modeValue;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            LoadError = "SETTINGS COULD NOT BE READ. USING DEFAULTS.";
            return new GameSettings();
        }
        return settings;
    }

    public void Save(GameSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new { settings.FpsCap, settings.UncappedFps, settings.ViewBobbing, settings.Difficulty, settings.GameMode }, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
