using System;
using System.Collections.Generic;
using System.IO;

namespace MDSM;

public sealed record WorldEntry(string Id, string Name, int? Seed, DateTime ModifiedUtc, string Error);

public sealed class WorldSaveManager
{
    public const string FileName = "world.dat";
    public string RootDirectory { get; }

    public WorldSaveManager(string rootDirectory = null)
    {
        if (rootDirectory == null)
        {
            string dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(dataDirectory))
                dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            if (!Path.IsPathFullyQualified(dataDirectory))
                throw new IOException("Could not determine the per-user application data directory.");
            rootDirectory = Path.Combine(dataDirectory, "MDSM", "Worlds");
        }
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string Create(WorldSave save)
    {
        WorldSerializer.ValidateName(save.Name);
        string id = Guid.NewGuid().ToString("N");
        try
        {
            Save(id, save);
            return id;
        }
        catch
        {
            // Only this newly-created, empty directory can be removed after a failed creation.
            try { Directory.Delete(WorldDirectory(id), false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public void Save(string id, WorldSave save)
    {
        string directory = WorldDirectory(id);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, FileName);
        string temporary = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WorldSerializer.Write(stream, save);
                stream.Flush(true);
            }
            // Same-directory rename/replacement keeps the previous valid save intact on write failure.
            if (File.Exists(destination))
                File.Replace(temporary, destination, null);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public WorldSave Load(string id)
    {
        using var stream = File.OpenRead(Path.Combine(WorldDirectory(id), FileName));
        return WorldSerializer.Read(stream);
    }

    public IReadOnlyList<WorldEntry> ListWorlds()
    {
        var entries = new List<WorldEntry>();
        if (!Directory.Exists(RootDirectory))
            return entries;
        foreach (string directory in Directory.EnumerateDirectories(RootDirectory))
        {
            string id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _))
                continue;
            string path = Path.Combine(directory, FileName);
            try
            {
                using var stream = File.OpenRead(path);
                WorldMetadata metadata = WorldSerializer.ReadMetadata(stream);
                entries.Add(new WorldEntry(id, metadata.Name, metadata.Seed, File.GetLastWriteTimeUtc(path), null));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                entries.Add(new WorldEntry(id, "UNREADABLE WORLD " + id[..8], null,
                    Directory.GetLastWriteTimeUtc(directory), exception.Message));
            }
        }
        entries.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));
        return entries;
    }

    private string WorldDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid world directory ID.", nameof(id));
        return Path.Combine(RootDirectory, id);
    }
}
