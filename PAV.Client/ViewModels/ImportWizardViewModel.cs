using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAV.Client.Services;
using PAV.Shared.Dtos;

namespace PAV.Client.ViewModels;

public partial class ImportWizardViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty] private int step = 1;
    [ObservableProperty] private string? filePath;
    [ObservableProperty] private string? fileName;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private ImportPreviewResponse? preview;
    [ObservableProperty] private ImportResult? result;
    [ObservableProperty] private bool errorsOnly;
    [ObservableProperty] private string colAssetTag = "Asset ID";
    [ObservableProperty] private string colCategory = "Category";
    [ObservableProperty] private string colManufacturer = "Manufacturer";
    [ObservableProperty] private string colModel = "Model";
    [ObservableProperty] private string colSerial = "Serial Number";
    [ObservableProperty] private string colHostname = "Hostname";
    [ObservableProperty] private string colLocation = "Location";
    [ObservableProperty] private string colStatus = "Status";
    [ObservableProperty] private string colAssigned = "Assigned User";
    [ObservableProperty] private string colPurchase = "Purchase Date";
    [ObservableProperty] private string colWarranty = "Warranty Expiry";
    [ObservableProperty] private string colRemarks = "Remarks";

    public ObservableCollection<string> Columns { get; } = [];
    public ObservableCollection<ImportPreviewRow> VisibleRows { get; } = [];
    public bool Imported { get; private set; }
    public event Action<bool>? CloseRequested;

    public string StepTitle => Step switch
    {
        1 => "Select Excel or CSV file",
        2 => "Map columns",
        3 => "Preview & validate",
        4 => "Import complete",
        _ => "Import"
    };

    public string PreviewSummary => Preview?.SummaryText ?? "";
    public bool HasPreviewErrors => Preview is { ErrorCount: > 0 };

    partial void OnStepChanged(int value) => OnPropertyChanged(nameof(StepTitle));

    partial void OnErrorsOnlyChanged(bool value) => RebuildVisible();

    [RelayCommand]
    private void Browse()
    {
        var path = Ui.OpenExcel();
        if (path is null) return;
        FilePath = path;
        FileName = System.IO.Path.GetFileName(path);
        Error = null;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        Error = null;
        if (Step == 1)
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                Error = "Select an Excel or CSV file first.";
                return;
            }
            Busy = true;
            try
            {
                var p = await api.PreviewImportAsync(FilePath, BuildMap());
                Columns.Clear();
                Columns.Add("(ignore)");
                foreach (var c in p.Columns) Columns.Add(c);
                AutoMap(p.Columns);
                Step = 2;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                Busy = false;
            }
            return;
        }

        if (Step == 2)
        {
            Busy = true;
            try
            {
                Preview = await api.PreviewImportAsync(FilePath!, BuildMap());
                ErrorsOnly = Preview.ErrorCount > 0;
                RebuildVisible();
                OnPropertyChanged(nameof(PreviewSummary));
                OnPropertyChanged(nameof(HasPreviewErrors));
                Step = 3;
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
    }

    [RelayCommand]
    private void Back()
    {
        Error = null;
        if (Step > 1 && Step < 4)
            Step--;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (Preview is null || Preview.ErrorCount > 0)
        {
            Error = Preview?.SummaryText ?? "Fix the invalid rows before importing. Nothing was written.";
            return;
        }
        Busy = true;
        try
        {
            Result = await api.ImportAsync(FilePath!, BuildMap());
            Imported = Result.Imported > 0;
            Step = 4;
        }
        catch (Exception ex)
        {
            Error = ex is ApiException apiEx && apiEx.Errors is { Count: > 0 }
                ? apiEx.Message + Environment.NewLine + string.Join(Environment.NewLine, apiEx.Errors.Take(40))
                : ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(Imported);

    private void RebuildVisible()
    {
        VisibleRows.Clear();
        if (Preview is null) return;
        var rows = ErrorsOnly ? Preview.Rows.Where(r => !r.IsValid) : Preview.Rows.AsEnumerable();
        foreach (var r in rows)
            VisibleRows.Add(r);
    }

    private ImportColumnMap BuildMap() => new()
    {
        AssetTag = Col(ColAssetTag),
        Category = Col(ColCategory),
        Manufacturer = Col(ColManufacturer),
        Model = Col(ColModel),
        SerialNumber = Col(ColSerial),
        Hostname = Col(ColHostname),
        Location = Col(ColLocation),
        Status = Col(ColStatus),
        AssignedUser = Col(ColAssigned),
        PurchaseDate = Col(ColPurchase),
        WarrantyExpiry = Col(ColWarranty),
        Remarks = Col(ColRemarks)
    };

    private static string Col(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "(ignore)" ? "" : value;

    private void AutoMap(List<string> columns)
    {
        string Pick(params string[] aliases)
        {
            foreach (var a in aliases)
            {
                var hit = columns.FirstOrDefault(c => c.Equals(a, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            return columns.FirstOrDefault(c =>
                aliases.Any(a => c.Replace(" ", "").Equals(a.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                   ?? "(ignore)";
        }

        ColAssetTag = Pick("Asset ID", "AssetID", "AssetTag", "Tag");
        if (columns.Any(c => c.Equals("Asset_Category", StringComparison.OrdinalIgnoreCase)))
            ColAssetTag = "(ignore)";
        ColCategory = Pick("Asset_Category", "Category");
        ColManufacturer = Pick("Manufacturer", "Make", "Brand");
        ColModel = Pick("Model", "Make_Model");
        ColSerial = Pick("Serial_Number", "Serial Number", "SerialNumber", "Serial", "S/N", "SN");
        ColHostname = Pick("Hostname", "Host", "Computer Name");
        ColLocation = Pick("Location");
        ColStatus = Pick("Status");
        ColAssigned = Pick("User Name", "Assigned User", "AssignedUser", "User", "Assigned To");
        ColPurchase = Pick("Purchase Date", "PurchaseDate", "Purchased");
        ColWarranty = Pick("Warranty Expiry", "WarrantyExpiry", "Warranty");
        ColRemarks = Pick("Remarks", "Notes", "Comments");
    }
}
