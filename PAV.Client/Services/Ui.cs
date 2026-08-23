using System.IO;
using System.Windows;
using Microsoft.Win32;
using PAV.Client.Views;

namespace PAV.Client.Services;

public static class Ui
{
    public static void Error(Exception ex)
    {
        if (ex is ApiException api && api.Errors is { Count: > 0 })
        {
            MessageDialog.Warning(
                api.Message + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, api.Errors));
            return;
        }

        MessageDialog.Warning(ex.Message);
    }

    public static void Info(string message)
    {
        MessageDialog.Info(message);
    }

    public static bool Confirm(string message)
    {
        // Split "Title?\n\nDetail" style messages into title + body when possible.
        var title = "Confirm";
        var body = message;
        var parts = message.Split(new[] { "\r\n\r\n", "\n\n" }, 2, StringSplitOptions.None);
        if (parts.Length == 2)
        {
            title = parts[0].Trim();
            body = parts[1].Trim();
        }
        else if (message.Contains('?') && message.IndexOf('?') < 80)
        {
            var i = message.IndexOf('?');
            title = message[..(i + 1)].Trim();
            body = message[(i + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(body))
                body = "This action can be undone from the bar at the bottom when available.";
        }

        return MessageDialog.Confirm(body, title);
    }

    public static string? PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the shared PAV folder" };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public static string? OpenExcel()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel or CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Select inventory file"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? SaveCsv(string suggested)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = suggested,
            Title = "Export current view"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static string? SaveExcel(string suggested)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            FileName = suggested,
            Title = "Export inventory"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public static async Task SaveBytes(string path, byte[] bytes)
    {
        await File.WriteAllBytesAsync(path, bytes);
    }
}
