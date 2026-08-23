using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;

namespace PAV.Client.ViewModels;

public partial class AssetDetailViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly ShellViewModel _shell;

    [ObservableProperty] private AssetDetailDto asset;
    public bool Changed { get; private set; }
    public bool OpenEditor { get; private set; }
    public event Action? CloseRequested;

    public AssetDetailViewModel(ApiClient api, ShellViewModel shell, AssetDetailDto asset)
    {
        _api = api;
        _shell = shell;
        Asset = asset;
    }

    public bool CanEdit => _shell.CanEdit;

    public IEnumerable<HistoryItem> HistoryItems =>
        Asset.History.Select(h => new HistoryItem(HistoryFormat.Title(h), HistoryFormat.Body(h)));

    public bool HasHistory => Asset.History.Count > 0;

    partial void OnAssetChanged(AssetDetailDto value)
    {
        OnPropertyChanged(nameof(HistoryItems));
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private void Edit()
    {
        if (!CanEdit) return;
        OpenEditor = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            Asset = await _api.AssetAsync(Asset.Id);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}

public record HistoryItem(string Title, string Body);

public static class HistoryFormat
{
    public static string Title(HistoryDto h)
    {
        var who = string.IsNullOrWhiteSpace(h.DisplayName) ? h.Username : h.DisplayName;
        return $"{h.Timestamp.ToLocalTime():dd MMM yyyy}  —  {who}";
    }

    public static string Body(HistoryDto h)
    {
        if (h.Action is HistoryAction.Created or HistoryAction.Imported or HistoryAction.Deleted
            && string.IsNullOrWhiteSpace(h.FieldName))
            return h.Action.ToString();

        if (string.IsNullOrWhiteSpace(h.FieldName))
            return h.Action.ToString();
        var oldv = string.IsNullOrWhiteSpace(h.OldValue) ? "—" : h.OldValue;
        var newv = string.IsNullOrWhiteSpace(h.NewValue) ? "—" : h.NewValue;
        return $"{h.FieldName} changed:{Environment.NewLine}{oldv}  →  {newv}";
    }
}
