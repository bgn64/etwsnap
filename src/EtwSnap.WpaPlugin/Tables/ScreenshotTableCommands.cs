using System.Diagnostics;
using EtwSnap.WpaPlugin.Models;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin.Tables;

internal static class ScreenshotTableCommands
{
    public static void Register(ITableBuilder tableBuilder, IReadOnlyList<ScreenshotRecord> rows)
    {
        tableBuilder.AddTableCommand("Open in Default Viewer", selectedRows =>
        {
            if (TryCreateViewStartInfo(rows, selectedRows, out var startInfo))
            {
                Process.Start(startInfo);
            }
        });
        tableBuilder.AddTableCommand("Reveal in File Explorer", selectedRows =>
        {
            if (TryCreateExplorerStartInfo(rows, selectedRows, out var startInfo))
            {
                Process.Start(startInfo);
            }
        });
    }

    internal static bool TryCreateViewStartInfo(
        IReadOnlyList<ScreenshotRecord> rows,
        IReadOnlyList<int> selectedRows,
        out ProcessStartInfo startInfo)
    {
        if (!TryGetSelectedImagePath(rows, selectedRows, out var imagePath))
        {
            startInfo = null!;
            return false;
        }

        startInfo = new ProcessStartInfo
        {
            FileName = imagePath,
            UseShellExecute = true,
        };
        return true;
    }

    internal static bool TryCreateExplorerStartInfo(
        IReadOnlyList<ScreenshotRecord> rows,
        IReadOnlyList<int> selectedRows,
        out ProcessStartInfo startInfo)
    {
        if (!TryGetSelectedImagePath(rows, selectedRows, out var imagePath))
        {
            startInfo = null!;
            return false;
        }

        startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add($"/select,{imagePath}");
        return true;
    }

    private static bool TryGetSelectedImagePath(
        IReadOnlyList<ScreenshotRecord> rows,
        IReadOnlyList<int> selectedRows,
        out string imagePath)
    {
        imagePath = string.Empty;
        if (selectedRows.Count != 1)
        {
            return false;
        }

        var rowIndex = selectedRows[0];
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return false;
        }

        var row = rows[rowIndex];
        if (row.Availability != ScreenshotAvailability.Saved ||
            string.IsNullOrWhiteSpace(row.ImagePath) ||
            !File.Exists(row.ImagePath))
        {
            return false;
        }

        imagePath = row.ImagePath;
        return true;
    }
}