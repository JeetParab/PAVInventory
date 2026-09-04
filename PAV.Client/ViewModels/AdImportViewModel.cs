using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class AdImportViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly string _path;

    [ObservableProperty] private AdImportPreviewDto preview;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool busy;
    public string? ResultSummary { get; private set; }
    public event Action<bool>? CloseRequested;

    public AdImportViewModel(ApiClient api, string path, AdImportPreviewDto preview)
    {
        _api = api;
        _path = path;
        Preview = preview;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!Preview.CanImport) return;
        Error = null;
        Busy = true;
        try
        {
            var result = await _api.ImportAdAsync(_path);
            ResultSummary = result.Summary;
            if (result.Errors.Count > 0)
                Error = string.Join(Environment.NewLine, result.Errors);
            else
                CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
