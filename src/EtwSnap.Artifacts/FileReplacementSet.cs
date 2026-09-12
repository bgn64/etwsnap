namespace EtwSnap.Artifacts;

public sealed class FileReplacementSet : IDisposable
{
    private readonly IReadOnlyList<(string FinalPath, string BackupPath)> _backups;
    private readonly List<(string TemporaryPath, string FinalPath)> _published = [];
    private bool _committed;

    private FileReplacementSet(IReadOnlyList<(string FinalPath, string BackupPath)> backups)
    {
        _backups = backups;
    }

    public static FileReplacementSet Create(IEnumerable<string> finalPaths)
    {
        var backups = new List<(string FinalPath, string BackupPath)>();
        try
        {
            foreach (var finalPath in finalPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(finalPath))
                {
                    continue;
                }
                var backupPath = finalPath + $".{Guid.NewGuid():N}.bak";
                File.Move(finalPath, backupPath, overwrite: false);
                backups.Add((finalPath, backupPath));
            }
            return new FileReplacementSet(backups);
        }
        catch
        {
            RestoreBackups(backups);
            throw;
        }
    }

    public void Publish(string temporaryPath, string finalPath)
    {
        File.Move(temporaryPath, finalPath, overwrite: false);
        _published.Add((temporaryPath, finalPath));
    }

    public void Commit()
    {
        _committed = true;
        foreach (var (_, backupPath) in _backups)
        {
            try
            {
                File.Delete(backupPath);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_committed)
        {
            return;
        }
        foreach (var (temporaryPath, finalPath) in _published.AsEnumerable().Reverse())
        {
            try
            {
                if (File.Exists(finalPath))
                {
                    File.Move(finalPath, temporaryPath, overwrite: true);
                }
            }
            catch
            {
            }
        }
        RestoreBackups(_backups);
    }

    private static void RestoreBackups(IEnumerable<(string FinalPath, string BackupPath)> backups)
    {
        foreach (var (finalPath, backupPath) in backups.Reverse())
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, finalPath, overwrite: true);
                }
            }
            catch
            {
            }
        }
    }
}