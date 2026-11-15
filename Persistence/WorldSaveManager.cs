using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using MDSM.Worlds;

namespace MDSM.Persistence;

public sealed record WorldEntry(string Id, string Name, int? Seed, DateTime ModifiedUtc, string Error,
    WorldSize? Size = null);

public sealed class WorldSaveManager
{
    public const string FileName = "world.dat";
    public string RootDirectory { get; }
    internal string ActiveWorldId { get; private set; }

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
        if(save.World.IsInfinite){InfiniteWorldPersistence.Save(directory,save);return;}
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
    internal System.Threading.Tasks.Task BeginInfiniteSave(string id,WorldSave save)
    {if(!save.World.IsInfinite)throw new ArgumentException("A streamed checkpoint requires an Infinite world.");return InfiniteWorldPersistence.BeginSave(WorldDirectory(id),save);}

    public WorldSave Load(string id)
    {
        string directory=WorldDirectory(id);
        using(var stream=File.OpenRead(Path.Combine(directory,FileName)))if(!InfiniteWorldPersistence.IsInfinite(stream))return WorldSerializer.Read(stream);
        return InfiniteWorldPersistence.Load(directory);
    }
    internal (string Id,WorldSave Save) CreateInfinite(int seed,string name,bool cheats,MDSM.Core.Difficulty difficulty,MDSM.Core.GameMode mode)
    {
        WorldSerializer.ValidateName(name);string id=Guid.NewGuid().ToString("N"),directory=WorldDirectory(id);
        var world=World.OpenStreamed(seed,Path.Combine(directory,"regions"));world.Climate.VariedGeneration=true;world.AllowCheats=cheats;world.SavedDifficulty=difficulty;world.SavedGameMode=mode;
        try
        {
            var deadline=Stopwatch.StartNew();bool ready=false;
            while(!ready)
            {
                world.UpdateStreaming(new(0,0));ready=true;
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)ready&=world.IsLoaded(new(x*16,0,z*16));
                if(world.ChunkLifecycle.Error!=null)throw new IOException("Could not initialize Infinite terrain.",world.ChunkLifecycle.Error);
                if(deadline.Elapsed.TotalSeconds>60)throw new IOException("Timed out initializing spawn terrain.");if(!ready)System.Threading.Thread.Sleep(1);
            }
            bool found=false;
            for(int r=0;r<=14&&!found;r++)for(int z=-r;z<=r&&!found;z++)for(int x=-r;x<=r&&!found;x++)
            {int y=world.SurfaceY(x,z);if(y<1||y+3>=World.Height||!Blocks.IsSolid(world.GetBlock(x,y,z))||world.GetBlock(x,y+1,z)!=BlockType.Air||world.GetBlock(x,y+2,z)!=BlockType.Air)continue;world.InfiniteSpawn=new(x+.5,y+1.01,z+.5);found=true;}
            if(!found)throw new IOException("No safe spawn found in initial terrain. Try a different seed.");
            var save=new WorldSave(world,name,world.Spawn,0,0,Inventories.Inventory.CreateNewWorld(),ExactPosition:world.InfiniteSpawn);Save(id,save);return(id,save);
        }
        catch{world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();throw;}
    }

    internal void SetActiveWorld(string id)
    {
        if (id != null) _ = WorldDirectory(id);
        ActiveWorldId = id;
    }

    public void Rename(string id, string name)
    {
        RequireInactive(id);
        name = WorldSerializer.ValidateName(name);
        WorldSave save = Load(id);
        try{Save(id, save with { Name = name });}
        finally{if(save.World.IsInfinite)save.World.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();}
    }

    public void Delete(string id)
    {
        RequireInactive(id);
        string directory = WorldDirectory(id);
        // Preflight every entry without following links. Only a validated direct child
        // of RootDirectory is eligible for recursive deletion.
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out string current))
        {
            RejectLink(current);
            foreach (string path in Directory.EnumerateFileSystemEntries(current))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("World directory contains a symbolic link or junction; deletion was canceled.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
        Directory.Delete(directory, true);
    }

    public void OpenSavesFolder()
    {
        Directory.CreateDirectory(RootDirectory);
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
            start = new ProcessStartInfo(RootDirectory) { UseShellExecute = true };
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(RootDirectory);
        }
        else throw new NotSupportedException("Opening the saves folder is unsupported on this platform.");
        using Process process = Process.Start(start);
        if (!OperatingSystem.IsWindows() && process != null && process.WaitForExit(1000) && process.ExitCode != 0)
            throw new IOException("The desktop could not open the saves folder. Check the system's default file manager.");
    }

    private void RequireInactive(string id)
    {
        _ = WorldDirectory(id);
        if (string.Equals(id, ActiveWorldId, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Close the currently loaded world before managing it.");
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
                _ = WorldDirectory(id);
                RejectLink(path);
                using var stream = File.OpenRead(path);
                WorldMetadata metadata = InfiniteWorldPersistence.IsInfinite(stream)?InfiniteWorldPersistence.Metadata(directory):WorldSerializer.ReadMetadata(stream);
                entries.Add(new WorldEntry(id, metadata.Name, metadata.Seed, File.GetLastWriteTimeUtc(path), null, metadata.Size));
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
        string directory = Path.GetFullPath(Path.Combine(RootDirectory, id));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(directory), Path.TrimEndingDirectorySeparator(RootDirectory), comparison))
            throw new ArgumentException("World directory must be directly inside the saves folder.", nameof(id));
        RejectLink(RootDirectory);
        RejectLink(directory);
        RejectLink(Path.Combine(directory, FileName));
        return directory;
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not allowed in world save paths.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
