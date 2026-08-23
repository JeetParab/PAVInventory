using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PAV.Client.Services;

public static class Ui
{
    public static void Error(Exception ex)
    {
        if (ex is ApiException api && api.Errors is { Count: > 0 })
        {
            MessageBox.Show(
                api.Message + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, api.Errors),
                "PAV Inventory",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(ex.Message, "PAV Inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static void Info(string message)
    {
        MessageBox.Show(message, "PAV Inventory", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static bool Confirm(string message)
    {
        return MessageBox.Show(message, "PAV Inventory", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
               MessageBoxResult.Yes;
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
