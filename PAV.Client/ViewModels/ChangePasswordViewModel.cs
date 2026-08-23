using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;

namespace PAV.Client.ViewModels;

public partial class ChangePasswordViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool forced;

    public string Title => Forced ? "Change your password" : "Change password";
    public string Intro => Forced
        ? "This account is still using a known default password. Set a new one (8+ characters) before continuing."
        : "Choose a new password with at least 8 characters. Do not use admin, engineer, or guest.";

    partial void OnForcedChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Intro));
    }

    public bool Saved { get; private set; }
    public event Action<bool>? CloseRequested;

    public async Task<bool> SaveAsync(string current, string next, string confirm)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(next))
        {
            Error = "Enter the current password and a new password.";
            return false;
        }
        if (!string.Equals(next, confirm, StringComparison.Ordinal))
        {
            Error = "New passwords do not match.";
            return false;
        }

        Busy = true;
        try
        {
            await api.ChangePasswordAsync(current, next);
            Saved = true;
            CloseRequested?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
