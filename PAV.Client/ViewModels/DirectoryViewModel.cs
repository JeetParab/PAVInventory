using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Client.Views;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class DirectoryViewModel(ApiClient api, ShellViewModel shell) : ObservableObject
{
    private readonly List<AdUserDto> _all = [];

    public ObservableCollection<AdUserDto> Users { get; } = [];

    [ObservableProperty] private AdUserDto? selected;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string summary = "Import the AD All Users workbook once. This list is for checking details — it is not the PAV Users tab.";

    public bool CanImportAd => shell.CanImport;
    public bool CanAdd => shell.CanAdd;
    public bool CanAddToPav => CanAdd && Selected is not null && !Selected.InPav;

    public async Task LoadAsync()
    {
        Loading = true;
        var keep = Selected?.Sam;
        try
        {
            var rows = await api.AdDirectoryAsync();
            _all.Clear();
            _all.AddRange(rows);
            OnPropertyChanged(nameof(CanImportAd));
            OnPropertyChanged(nameof(CanAdd));
            ApplyFilter();
            if (keep is { } sam)
                Selected = Users.FirstOrDefault(u => string.Equals(u.Sam, sam, StringComparison.OrdinalIgnoreCase))
                           ?? Users.FirstOrDefault();
            else
                Selected = Users.FirstOrDefault();
            Summary = _all.Count == 0
                ? "No AD user ids stored yet. Import AD users (the ADMP All Users workbook) — one time."
                : $"{_all.Count:N0} AD user ids. Search by user id, name, email, department or employee ID. PAV Users is a separate list of people you assign kit to.";
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            Loading = false;
        }
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(AdUserDto? value) => OnPropertyChanged(nameof(CanAddToPav));

    private void ApplyFilter()
    {
        var s = Search?.Trim() ?? "";
        IEnumerable<AdUserDto> q = _all;
        if (s.Length > 0)
        {
            q = _all.Where(u =>
                u.Sam.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (u.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Email?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.Department?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (u.EmployeeId?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var keep = Selected?.Sam;
        Users.Clear();
        foreach (var u in q) Users.Add(u);
        if (s.Length > 0)
            Selected = Users.FirstOrDefault();
        else if (keep is { } sam)
            Selected = Users.FirstOrDefault(u => string.Equals(u.Sam, sam, StringComparison.OrdinalIgnoreCase))
                       ?? Users.FirstOrDefault();
        else
            Selected = Users.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ImportAdAsync()
    {
        if (!CanImportAd) return;
        var path = Ui.OpenExcel();
        if (path is null) return;
        try
        {
            var preview = await api.PreviewAdImportAsync(path);
            var vm = new AdImportViewModel(api, path, preview);
            var win = new AdImportWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                if (!string.IsNullOrWhiteSpace(vm.ResultSummary))
                    Ui.Info(vm.ResultSummary);
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task AddToPavAsync()
    {
        if (!CanAddToPav || Selected is null) return;
        try
        {
            var person = await api.EnsurePersonFromAdAsync(Selected.Sam);
            if (person is null)
            {
                Ui.Info("Could not add that user id to PAV Users.");
                return;
            }
            Ui.Info($"{person.Name} is now on the Users tab.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}
