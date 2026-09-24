using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;

namespace PAV.Client.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private string databasePath;
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool needsSetup;
    [ObservableProperty] private string statusLine = "Point this app at the shared folder so everyone uses the same database.";

    public string PrimaryAction => NeedsSetup ? "Create administrator" : "Sign in";
    public string Title => NeedsSetup ? "Create administrator" : "Sign in";

    public LoginViewModel(ApiClient api, ClientConfig config)
    {
        _api = api;
        databasePath = config.DatabasePath;
    }

    partial void OnNeedsSetupChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryAction));
        OnPropertyChanged(nameof(Title));
        StatusLine = value
            ? "This database has no users yet. Create the first administrator. Choose a password that is not admin / engineer / guest."
            : "Use a PAV account. Point this app at the shared folder so everyone uses the same database.";
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = Ui.PickFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        DatabasePath = folder;
        await PrepareAsync();
    }

    public async Task PrepareAsync()
    {
        Error = null;
        try
        {
            using var _ = PAV.Core.Services.Perf.Measure("Login.Prepare");
            await _api.SetDatabasePath(DatabasePath);
            NeedsSetup = await _api.NeedsSetupAsync();
        }
        catch (Exception ex)
        {
            NeedsSetup = false;
            Error = ex.Message;
        }
    }

    public async Task<bool> SignInAsync(string password, string? confirmPassword)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            Error = NeedsSetup ? "Enter a username and password." : "Enter a username and password.";
            return false;
        }

        Busy = true;
        try
        {
            await _api.SetDatabasePath(DatabasePath);
            NeedsSetup = await _api.NeedsSetupAsync();

            if (NeedsSetup)
            {
                if (string.IsNullOrWhiteSpace(DisplayName))
                {
                    Error = "Enter a display name.";
                    return false;
                }
                if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
                {
                    Error = "Passwords do not match.";
                    return false;
                }
                await _api.SetupAdministratorAsync(Username.Trim(), DisplayName.Trim(), password);
                return true;
            }

            await _api.LoginAsync(Username.Trim(), password);
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
}
