using Microsoft.Data.Sqlite;
using PAV.Core.Data;
using PAV.Shared.Dtos;

namespace PAV.Core.Services;

public class BackupService(PavDatabase pav, IWriteLock writeLock)
{
    public string BackupDirectory
    {
        get
        {
            Directory.CreateDirectory(pav.BackupDirectory);
            return pav.BackupDirectory;
        }
    }

    public string DatabasePath => pav.DatabasePath;

    public List<BackupInfo> List()
    {
        if (!Directory.Exists(BackupDirectory))
            return [];
        return Directory.GetFiles(BackupDirectory, "PAVInventory_*.*")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo
            {
                FileName = f.Name,
                CreatedAt = f.CreationTimeUtc,
                SizeBytes = f.Length
            })
            .ToList();
    }

    public Task<BackupInfo> BackupNowAsync() =>
        writeLock.WriteAsync(() => Task.FromResult(FileCopySqlite()));

    public Task RestoreAsync(string fileName) =>
        writeLock.WriteAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName.Contains("..") ||
                !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                throw new AppException(400, "validation", "Backup file name is not valid.");

            var src = Path.Combine(BackupDirectory, fileName);
            if (!File.Exists(src))
                throw new AppException(404, "not_found", "Backup file was not found.");

            var safety = Path.Combine(BackupDirectory, $"PAVInventory_pre-restore_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");
            SqliteConnection.ClearAllPools();
            if (File.Exists(DatabasePath))
                File.Copy(DatabasePath, safety, overwrite: false);

            File.Copy(src, DatabasePath, overwrite: true);
            foreach (var extra in new[] { DatabasePath + "-wal", DatabasePath + "-shm", DatabasePath + "-journal" })
            {
                if (File.Exists(extra)) File.Delete(extra);
            }
            pav.Invalidate();
            return Task.CompletedTask;
        });

    public void PruneUnlocked()
    {
        var files = Directory.GetFiles(BackupDirectory, "PAVInventory_*.*")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(30)
            .ToList();
        foreach (var f in files)
        {
            try { f.Delete(); } catch { /* ignore */ }
        }
    }

    private BackupInfo FileCopySqlite()
    {
        SqliteConnection.ClearAllPools();
        var name = $"PAVInventory_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db";
        var dest = Path.Combine(BackupDirectory, name);
        if (!File.Exists(DatabasePath))
            throw new AppException(500, "error", "Database file was not found.");
        File.Copy(DatabasePath, dest, overwrite: false);
        PruneUnlocked();
        var info = new FileInfo(dest);
        return new BackupInfo
        {
            FileName = info.Name,
            CreatedAt = info.CreationTimeUtc,
            SizeBytes = info.Length
        };
    }
}
