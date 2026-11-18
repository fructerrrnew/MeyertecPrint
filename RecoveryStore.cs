namespace MeyertecPrint;

/// <summary>One atomic recovery snapshot per running application instance.</summary>
public sealed class RecoveryStore : IDisposable
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeyertecPrint", "Recovery");
    private readonly string _id = Guid.NewGuid().ToString("N");
    private FileStream? _lock;
    public string SnapshotPath => Path.Combine(_directory, _id + ".mprj");
    private string LockPath => Path.Combine(_directory, _id + ".lock");

    public RecoveryStore()
    {
        Directory.CreateDirectory(_directory);
        _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Save(ProjectFile project) => ProjectFileIo.SaveRecovery(project, SnapshotPath);
    public void Clear() { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); }
    public string? LatestAbandoned()
    {
        foreach (var path in Directory.EnumerateFiles(_directory, "*.mprj").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (string.Equals(path, SnapshotPath, StringComparison.OrdinalIgnoreCase)) continue;
            var lockPath = Path.ChangeExtension(path, ".lock");
            try
            {
                using var probe = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return path;
            }
            catch (IOException) { /* Snapshot belongs to a live instance. */ }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
    public void Dispose()
    {
        _lock?.Dispose(); _lock = null;
        try { if (File.Exists(LockPath)) File.Delete(LockPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
