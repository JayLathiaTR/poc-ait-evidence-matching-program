using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AitEvidenceMatching.App.Models;
using AitEvidenceMatching.App.Services;
using ExcelDataReader;

namespace AitEvidenceMatching.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly OcrClient _ocrClient;
    private readonly Dictionary<string, string> _ocrTextByDocId = new();
    private const string MissingDocumentIdMessage = "Document ID is not available";
    private static readonly IBrush MissingRowBrush = new SolidColorBrush(Color.Parse("#33FF5252"));
    private static readonly IBrush MatchCellBrush = new SolidColorBrush(Color.Parse("#334CAF50"));

    private sealed record ScoredCandidate<T>(T Value, int Score);

    private enum DocType
    {
        Invoice,
        Bol,
        Po,
        Je,
        Tb,
        Other,
    }

    public ObservableCollection<SampleRowItem> SampleRows { get; } = [];

    public ObservableCollection<EvidenceDocItem> EvidenceDocs { get; } = [];

    public ObservableCollection<HeaderOption> PendingHeaders { get; } = [];

    [ObservableProperty]
    private HeaderOption? selectedDocumentIdHeader;

    [ObservableProperty]
    private HeaderOption? selectedCustomerNameHeader;

    [ObservableProperty]
    private HeaderOption? selectedTransactionDateHeader;

    [ObservableProperty]
    private HeaderOption? selectedAmountHeader;

    [ObservableProperty]
    private bool outputDocumentIdSelected = true;

    [ObservableProperty]
    private bool outputCustomerNameSelected;

    [ObservableProperty]
    private bool outputAmountSelected;

    [ObservableProperty]
    private bool outputDocumentDateSelected;

    [ObservableProperty]
    private bool outputShippingDateSelected;

    [ObservableProperty]
    private bool outputShippingAddressSelected;

    [ObservableProperty]
    private bool hasPendingImport;

    [ObservableProperty]
    private string pendingSampleFilename = string.Empty;

    [ObservableProperty]
    private bool isGenerating;

    [ObservableProperty]
    private string generateError = string.Empty;

    [ObservableProperty]
    private string importStatus = "No sample file imported yet.";

    [ObservableProperty]
    private string title = "AIT Evidence Matching (Desktop)";

    [ObservableProperty]
    private string ocrStatus = "Starting...";

    [ObservableProperty]
    private string ocrStatusDetail = "Initializing OCR service...";

    [ObservableProperty]
    private string apiBaseUrl = "http://127.0.0.1:3001";

    [ObservableProperty]
    private string notes = "Import samples, map required fields, add evidence docs, then run OCR verification.";

    [ObservableProperty]
    private string logTail = string.Empty;

    [ObservableProperty]
    private EvidenceDocItem? selectedEvidenceDoc;

    [ObservableProperty]
    private Bitmap? selectedEvidenceBitmap;

    [ObservableProperty]
    private string selectedEvidencePreviewTitle = "Select an evidence file to preview.";

    [ObservableProperty]
    private string selectedEvidencePreviewMessage = "Image preview is available for image evidence. PDF files can be opened externally.";

    [ObservableProperty]
    private bool selectedEvidenceIsImage;

    [ObservableProperty]
    private bool selectedEvidenceIsPdf;

    [ObservableProperty]
    private double generationProgressPercent;

    [ObservableProperty]
    private string generationProgressText = "Ready to generate.";

    [ObservableProperty]
    private bool clearEvidenceConfirmVisible;

    public IRelayCommand ApplySampleImportCommand { get; }

    public IRelayCommand ClearPendingImportCommand { get; }

    public IAsyncRelayCommand GenerateCommand { get; }

    public IRelayCommand OpenSelectedEvidenceCommand { get; }

    public IRelayCommand ClearEvidenceFilesCommand { get; }

    public IRelayCommand RequestClearEvidenceFilesCommand { get; }

    public IRelayCommand ConfirmClearEvidenceFilesCommand { get; }

    public IRelayCommand CancelClearEvidenceFilesCommand { get; }

    public IRelayCommand RemoveSelectedEvidenceCommand { get; }

    public IRelayCommand<EvidenceDocItem?> RemoveEvidenceCommand { get; }

    public MainWindowViewModel(OcrClient ocrClient)
    {
        _ocrClient = ocrClient;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ApplySampleImportCommand = new RelayCommand(ApplySampleImport);
        ClearPendingImportCommand = new RelayCommand(ClearPendingImport);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsGenerating && HasAnyOutputSelected);
        OpenSelectedEvidenceCommand = new RelayCommand(OpenSelectedEvidence, () => SelectedEvidenceDoc is not null);
        ClearEvidenceFilesCommand = new RelayCommand(ClearEvidenceFiles);
        RequestClearEvidenceFilesCommand = new RelayCommand(RequestClearEvidenceFiles);
        ConfirmClearEvidenceFilesCommand = new RelayCommand(ConfirmClearEvidenceFiles);
        CancelClearEvidenceFilesCommand = new RelayCommand(CancelClearEvidenceFiles);
        RemoveSelectedEvidenceCommand = new RelayCommand(RemoveSelectedEvidence);
        RemoveEvidenceCommand = new RelayCommand<EvidenceDocItem?>(RemoveEvidence);
    }

    public bool HasSelectedEvidence => SelectedEvidenceDoc is not null;

    public bool ShowEvidencePlaceholder => !HasSelectedEvidence;

    public bool ShowEvidenceImage => HasSelectedEvidence && SelectedEvidenceIsImage && SelectedEvidenceBitmap is not null;

    public bool ShowEvidencePdf => HasSelectedEvidence && SelectedEvidenceIsPdf;

    public bool ShowEvidenceUnsupported => HasSelectedEvidence && !SelectedEvidenceIsImage && !SelectedEvidenceIsPdf;

    public bool HasSelectedEvidenceForBulkDelete => EvidenceDocs.Any(x => x.IsSelected);

    public bool HasEvidenceFiles => EvidenceDocs.Count > 0;

    public bool CanRemoveSelectedEvidence => CanEditInputs && HasSelectedEvidenceForBulkDelete;

    public bool CanClearEvidenceFiles => CanEditInputs && HasEvidenceFiles;

    public bool ShowClearEvidenceConfirm => ClearEvidenceConfirmVisible;

    public bool CanApplySampleImport =>
        HasPendingImport &&
        SelectedDocumentIdHeader is not null &&
        SelectedDocumentIdHeader.Index >= 0;

    public bool HasAnyOutputSelected =>
        OutputDocumentIdSelected ||
        OutputCustomerNameSelected ||
        OutputAmountSelected ||
        OutputDocumentDateSelected ||
        OutputShippingDateSelected ||
        OutputShippingAddressSelected;

    public bool CanEditInputs => !IsGenerating;

    public bool ShowVerifiedDocumentIdColumn => OutputDocumentIdSelected;

    public bool ShowVerifiedCustomerNameColumn => OutputCustomerNameSelected;

    public bool ShowVerifiedDocumentDateColumn => OutputDocumentDateSelected;

    public bool ShowVerifiedAmountColumn => OutputAmountSelected;

    public bool ShowVerifiedShippingDateColumn => OutputShippingDateSelected;

    public bool ShowVerifiedShippingAddressColumn => OutputShippingAddressSelected;

    public bool ShowDocumentIdStatusColumn => OutputDocumentIdSelected;

    public bool ShowCustomerNameStatusColumn => OutputCustomerNameSelected;

    public bool ShowAmountStatusColumn => OutputAmountSelected;

    public bool ShowDocumentDateStatusColumn => OutputDocumentDateSelected;

    public bool ShowShippingDateStatusColumn => OutputShippingDateSelected;

    public bool ShowShippingAddressStatusColumn => OutputShippingAddressSelected;

    public void SetStatus(string status, string detail)
    {
        OcrStatus = status;
        OcrStatusDetail = detail;
    }

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogTail = line;
    }

    public void SetPendingSample(DataTable table, string filename)
    {
        PendingHeaders.Clear();
        PendingHeaders.Add(new HeaderOption { Index = -1, Label = "(Not mapped)" });

        PendingSampleFilename = filename;

        if (table.Columns.Count == 0 || table.Rows.Count == 0)
        {
            GenerateError = "Sample file must include a header row and at least one data row.";
            HasPendingImport = false;
            return;
        }

        for (var i = 0; i < table.Columns.Count; i += 1)
        {
            var name = table.Columns[i].ColumnName;
            PendingHeaders.Add(new HeaderOption
            {
                Index = i,
                Label = string.IsNullOrWhiteSpace(name) ? $"Column {i + 1}" : name,
            });
        }

        SelectedDocumentIdHeader = AutoMap(PendingHeaders, "document id", "invoice id", "document", "doc id", "inv id");
        SelectedCustomerNameHeader = AutoMap(PendingHeaders, "customer name", "customer", "client", "bill to", "sold to");
        SelectedTransactionDateHeader = AutoMap(PendingHeaders, "transaction date", "document date", "invoice date", "date");
        SelectedAmountHeader = AutoMap(PendingHeaders, "original transaction amount", "amount", "invoice amount", "total", "transaction amount");

        HasPendingImport = true;
        GenerateError = string.Empty;

        OnPropertyChanged(nameof(CanApplySampleImport));
    }

    public void AddEvidenceFiles(IEnumerable<string> filePaths)
    {
        var addedAny = false;
        foreach (var path in filePaths)
        {
            if (EvidenceDocs.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var evidence = new EvidenceDocItem
            {
                FilePath = path,
                Name = Path.GetFileName(path),
                DocumentId = TryExtractDocumentIdFromFilename(Path.GetFileName(path)),
            };
            evidence.PropertyChanged += OnEvidenceDocPropertyChanged;
            EvidenceDocs.Add(evidence);
            addedAny = true;
        }

        if (addedAny && SelectedEvidenceDoc is null && EvidenceDocs.Count > 0)
        {
            SelectedEvidenceDoc = EvidenceDocs[0];
        }

        OnPropertyChanged(nameof(HasSelectedEvidenceForBulkDelete));
        OnPropertyChanged(nameof(HasEvidenceFiles));
        OnPropertyChanged(nameof(CanRemoveSelectedEvidence));
        OnPropertyChanged(nameof(CanClearEvidenceFiles));
    }

    private void OnEvidenceDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(EvidenceDocItem.IsSelected))
        {
            OnPropertyChanged(nameof(HasSelectedEvidenceForBulkDelete));
            OnPropertyChanged(nameof(CanRemoveSelectedEvidence));
        }
    }

    private void ClearEvidenceFiles()
    {
        if (EvidenceDocs.Count == 0)
        {
            return;
        }

        RemoveEvidenceDocs(EvidenceDocs.ToList());
        ClearEvidenceConfirmVisible = false;
    }

    private void RequestClearEvidenceFiles()
    {
        if (EvidenceDocs.Count == 0)
        {
            return;
        }

        ClearEvidenceConfirmVisible = true;
    }

    private void ConfirmClearEvidenceFiles()
    {
        ClearEvidenceFiles();
    }

    private void CancelClearEvidenceFiles()
    {
        ClearEvidenceConfirmVisible = false;
    }

    private void RemoveSelectedEvidence()
    {
        var selected = EvidenceDocs.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        RemoveEvidenceDocs(selected);
    }

    private void RemoveEvidence(EvidenceDocItem? evidence)
    {
        if (evidence is null)
        {
            return;
        }

        RemoveEvidenceDocs([evidence]);
    }

    private void RemoveEvidenceDocs(IReadOnlyList<EvidenceDocItem> docs)
    {
        if (docs.Count == 0)
        {
            return;
        }

        var removeSet = new HashSet<EvidenceDocItem>(docs);
        var shouldReselect = SelectedEvidenceDoc is not null && removeSet.Contains(SelectedEvidenceDoc);

        foreach (var doc in docs)
        {
            doc.PropertyChanged -= OnEvidenceDocPropertyChanged;
            _ocrTextByDocId.Remove(doc.Id);
            EvidenceDocs.Remove(doc);
        }

        if (EvidenceDocs.Count == 0)
        {
            SelectedEvidenceDoc = null;
            ClearEvidenceConfirmVisible = false;
        }
        else if (shouldReselect)
        {
            SelectedEvidenceDoc = EvidenceDocs[0];
        }

        OnPropertyChanged(nameof(HasSelectedEvidenceForBulkDelete));
        OnPropertyChanged(nameof(HasEvidenceFiles));
        OnPropertyChanged(nameof(CanRemoveSelectedEvidence));
        OnPropertyChanged(nameof(CanClearEvidenceFiles));
    }

    partial void OnSelectedEvidenceDocChanged(EvidenceDocItem? value)
    {
        LoadEvidencePreview(value);
    }

    public DataTable ParseSampleFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".csv")
        {
            var lines = File.ReadAllLines(filePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(ParseCsvLine)
                .ToList();

            return CacheAndReturn(BuildTableFromRawRows(lines));
        }

        if (ext is not ".xlsx" and not ".xls")
        {
            throw new InvalidOperationException("Unsupported file type. Use CSV or Excel (.xlsx/.xls).");
        }

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false,
            },
        });

        var table = dataSet.Tables.Count > 0 ? dataSet.Tables[0] : null;
        if (table is null)
        {
            throw new InvalidOperationException("Sample file did not contain any worksheet data.");
        }

        var rawRows = new List<List<string>>();
        foreach (DataRow row in table.Rows)
        {
            var values = new List<string>();
            for (var i = 0; i < table.Columns.Count; i += 1)
            {
                values.Add((row[i]?.ToString() ?? string.Empty).Trim());
            }

            rawRows.Add(values);
        }

        return CacheAndReturn(BuildTableFromRawRows(rawRows));
    }

    partial void OnSelectedDocumentIdHeaderChanged(HeaderOption? value)
    {
        OnPropertyChanged(nameof(CanApplySampleImport));
    }

    partial void OnHasPendingImportChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplySampleImport));
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditInputs));
        OnPropertyChanged(nameof(CanRemoveSelectedEvidence));
        OnPropertyChanged(nameof(CanClearEvidenceFiles));
        GenerateCommand.NotifyCanExecuteChanged();
        if (!value && GenerationProgressPercent < 100)
        {
            GenerationProgressText = "Ready to generate.";
        }
        if (value)
        {
            ClearEvidenceConfirmVisible = false;
        }
    }

    partial void OnClearEvidenceConfirmVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowClearEvidenceConfirm));
    }

    partial void OnOutputDocumentIdSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    partial void OnOutputCustomerNameSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    partial void OnOutputDocumentDateSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    partial void OnOutputAmountSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    partial void OnOutputShippingDateSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    partial void OnOutputShippingAddressSelectedChanged(bool value)
    {
        NotifyOutputSelectionChanged();
    }

    private void NotifyOutputSelectionChanged()
    {
        OnPropertyChanged(nameof(HasAnyOutputSelected));
        OnPropertyChanged(nameof(ShowVerifiedDocumentIdColumn));
        OnPropertyChanged(nameof(ShowVerifiedCustomerNameColumn));
        OnPropertyChanged(nameof(ShowVerifiedDocumentDateColumn));
        OnPropertyChanged(nameof(ShowVerifiedAmountColumn));
        OnPropertyChanged(nameof(ShowVerifiedShippingDateColumn));
        OnPropertyChanged(nameof(ShowVerifiedShippingAddressColumn));
        OnPropertyChanged(nameof(ShowDocumentIdStatusColumn));
        OnPropertyChanged(nameof(ShowCustomerNameStatusColumn));
        OnPropertyChanged(nameof(ShowDocumentDateStatusColumn));
        OnPropertyChanged(nameof(ShowAmountStatusColumn));
        OnPropertyChanged(nameof(ShowShippingDateStatusColumn));
        OnPropertyChanged(nameof(ShowShippingAddressStatusColumn));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private void LoadEvidencePreview(EvidenceDocItem? evidence)
    {
        SelectedEvidenceBitmap = null;
        SelectedEvidenceIsImage = false;
        SelectedEvidenceIsPdf = false;

        if (evidence is null)
        {
            SelectedEvidencePreviewTitle = "Select an evidence file to preview.";
            SelectedEvidencePreviewMessage = "Image preview is available for image evidence. PDF files can be opened externally.";
            NotifyEvidencePreviewVisibilityChanged();
            OpenSelectedEvidenceCommand.NotifyCanExecuteChanged();
            return;
        }

        SelectedEvidencePreviewTitle = evidence.Name;

        var ext = Path.GetExtension(evidence.FilePath).ToLowerInvariant();
        var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff";
        var isPdf = ext == ".pdf";

        SelectedEvidenceIsImage = isImage;
        SelectedEvidenceIsPdf = isPdf;

        if (isImage)
        {
            try
            {
                SelectedEvidenceBitmap = new Bitmap(evidence.FilePath);
                SelectedEvidencePreviewMessage = "";
            }
            catch (Exception ex)
            {
                SelectedEvidenceBitmap = null;
                SelectedEvidencePreviewMessage = $"Unable to load image preview: {ex.Message}";
            }
        }
        else if (isPdf)
        {
            SelectedEvidencePreviewMessage = "PDF preview is opened externally in this desktop version.";
        }
        else
        {
            SelectedEvidencePreviewMessage = "Preview is not available for this file type.";
        }

        NotifyEvidencePreviewVisibilityChanged();
        OpenSelectedEvidenceCommand.NotifyCanExecuteChanged();
    }

    private void NotifyEvidencePreviewVisibilityChanged()
    {
        OnPropertyChanged(nameof(HasSelectedEvidence));
        OnPropertyChanged(nameof(ShowEvidencePlaceholder));
        OnPropertyChanged(nameof(ShowEvidenceImage));
        OnPropertyChanged(nameof(ShowEvidencePdf));
        OnPropertyChanged(nameof(ShowEvidenceUnsupported));
    }

    private void OpenSelectedEvidence()
    {
        if (SelectedEvidenceDoc is null || string.IsNullOrWhiteSpace(SelectedEvidenceDoc.FilePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedEvidenceDoc.FilePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            GenerateError = $"Unable to open selected evidence file: {ex.Message}";
        }
    }

    private HeaderOption AutoMap(IEnumerable<HeaderOption> options, params string[] aliases)
    {
        var mapped = options
            .Where(x => x.Index >= 0)
            .Select(x => new
            {
                Option = x,
                Normalized = NormalizeLabel(x.Label),
            })
            .ToList();

        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeLabel(alias);
            var exact = mapped.FirstOrDefault(x => x.Normalized == normalizedAlias);
            if (exact is not null)
            {
                return exact.Option;
            }
        }

        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeLabel(alias);
            var contains = mapped.FirstOrDefault(x => x.Normalized.Contains(normalizedAlias, StringComparison.Ordinal));
            if (contains is not null)
            {
                return contains.Option;
            }
        }

        return options.First();
    }

    private static string NormalizeLabel(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
    }

    private void ClearPendingImport()
    {
        PendingHeaders.Clear();
        HasPendingImport = false;
        PendingSampleFilename = string.Empty;
        SelectedDocumentIdHeader = null;
        SelectedCustomerNameHeader = null;
        SelectedTransactionDateHeader = null;
        SelectedAmountHeader = null;
        OnPropertyChanged(nameof(CanApplySampleImport));
    }

    private void ApplySampleImport()
    {
        try
        {
            GenerateError = string.Empty;
            if (!CanApplySampleImport)
            {
                GenerateError = "Document ID is required. Please map a column to Document ID before import.";
                ImportStatus = "Import blocked: Document ID mapping missing.";
                return;
            }

            if (_pendingDataTable is null)
            {
                GenerateError = "No pending sample data loaded.";
                ImportStatus = "Import blocked: pending sample data is null.";
                return;
            }

            SampleRows.Clear();

            var idxDocumentId = SelectedDocumentIdHeader?.Index ?? -1;
            var idxCustomer = SelectedCustomerNameHeader?.Index ?? -1;
            var idxDate = SelectedTransactionDateHeader?.Index ?? -1;
            var idxAmount = SelectedAmountHeader?.Index ?? -1;

            var rowNumber = 1;
            foreach (DataRow row in _pendingDataTable.Rows)
            {
                var documentId = ReadCell(row, idxDocumentId).Trim();
                var customer = ReadCell(row, idxCustomer);
                var date = ReadCell(row, idxDate);
                var amountRaw = ReadCell(row, idxAmount);

                var amount = ParseAmount(amountRaw);

                SampleRows.Add(new SampleRowItem
                {
                    TransactionId = $"TRX-{rowNumber:0000}",
                    DocumentId = documentId,
                    CustomerName = customer,
                    TransactionDate = date,
                    OriginalTransactionAmount = amount,
                });

                rowNumber += 1;
            }

            if (SampleRows.Count == 0)
            {
                GenerateError = "No rows were imported. Check that your sample file has a valid header row and at least one data row.";
                ImportStatus = $"Import produced 0 rows. Pending table rows: {_pendingDataTable.Rows.Count}.";
                return;
            }

            ImportStatus = $"Imported {SampleRows.Count} row(s) from '{PendingSampleFilename}'.";
            ClearPendingImport();
            GenerateError = string.Empty;
        }
        catch (Exception ex)
        {
            GenerateError = ex.Message;
            ImportStatus = $"Import failed with exception: {ex.Message}";
        }
    }

    private async Task GenerateAsync()
    {
        if (!HasAnyOutputSelected)
        {
            GenerateError = "Select at least one output field before generating.";
            return;
        }

        if (SampleRows.Count == 0)
        {
            GenerateError = "Import sample rows before generating.";
            return;
        }

        if (EvidenceDocs.Count == 0)
        {
            GenerateError = "Add at least one evidence document before generating.";
            return;
        }

        IsGenerating = true;
        GenerateError = string.Empty;
        GenerationProgressPercent = 0;
        GenerationProgressText = "Starting generation...";

        try
        {
            var docsToOcr = EvidenceDocs.Count(x => !_ocrTextByDocId.ContainsKey(x.Id));
            var totalSteps = Math.Max(1, docsToOcr + SampleRows.Count);
            var completedSteps = 0;

            foreach (var doc in EvidenceDocs)
            {
                if (_ocrTextByDocId.ContainsKey(doc.Id))
                {
                    continue;
                }

                var response = await _ocrClient.ExtractAsync(doc.FilePath, CancellationToken.None);
                _ocrTextByDocId[doc.Id] = response.Text ?? string.Empty;
                completedSteps += 1;
                UpdateGenerationProgress(completedSteps, totalSteps, "OCR processing");
            }

            foreach (var row in SampleRows)
            {
                ResetComputedOutputs(row);

                if (string.IsNullOrWhiteSpace(row.DocumentId))
                {
                    if (OutputDocumentIdSelected)
                    {
                        row.VerifiedDocumentId = MissingDocumentIdMessage;
                        row.DocumentIdStatus = "Missing";
                    }

                    row.RowNote = "Row skipped: missing Document ID.";
                    ApplyCellHighlights(row);
                    ApplyRowHighlight(row);
                    continue;
                }

                var best = FindBestEvidence(row);
                if (best is null)
                {
                    row.LinkedEvidenceName = string.Empty;
                    if (OutputDocumentIdSelected) row.DocumentIdStatus = "No match";
                    if (OutputCustomerNameSelected) row.CustomerNameStatus = "No match";
                    if (OutputAmountSelected) row.AmountStatus = "No match";
                    if (OutputDocumentDateSelected) row.DocumentDateStatus = "No match";
                    if (OutputShippingDateSelected) row.ShippingDateStatus = "No match";
                    if (OutputShippingAddressSelected) row.ShippingAddressStatus = "No match";
                    row.RowNote = "No evidence matched this row.";
                    ApplyCellHighlights(row);
                    ApplyRowHighlight(row);
                    continue;
                }

                row.LinkedEvidenceName = best.Name;

                var ocrText = _ocrTextByDocId[best.Id];

                if (OutputDocumentIdSelected)
                {
                    row.VerifiedDocumentId = row.DocumentId;
                    row.DocumentIdStatus = IsDocumentIdMatch(row) ? "Match" : "Mismatch";
                }

                if (OutputCustomerNameSelected)
                {
                    var customerCandidate = ExtractCustomerCandidate(ocrText, row.CustomerName);
                    row.VerifiedCustomerName = customerCandidate?.Value ?? string.Empty;
                    row.CustomerNameStatus = string.IsNullOrWhiteSpace(row.VerifiedCustomerName)
                        ? "No match"
                        : IsCustomerNameMatch(row) ? "Match" : "Review";
                }

                if (OutputDocumentDateSelected)
                {
                    var dateCandidate = ExtractDocumentDateCandidate(ocrText, row.TransactionDate);
                    row.VerifiedDocumentDate = dateCandidate?.Value ?? FirstDateToken(ocrText);
                    row.DocumentDateStatus = string.IsNullOrWhiteSpace(row.VerifiedDocumentDate)
                        ? "No match"
                        : IsDateMatch(row) ? "Match" : "Review";
                }

                var includeShipping = OutputShippingDateSelected || OutputShippingAddressSelected;
                if (includeShipping)
                {
                    var relatedShippingDoc = FindRelatedShippingDoc(best.Id, ocrText, row.DocumentId);
                    var shippingText = relatedShippingDoc is not null && _ocrTextByDocId.TryGetValue(relatedShippingDoc.Id, out var relatedText)
                        ? relatedText
                        : string.Empty;
                    var shipping = !string.IsNullOrWhiteSpace(shippingText)
                        ? ExtractShippingFieldsFromOcr(shippingText)
                        : (Date: string.Empty, Address: string.Empty);

                    if (OutputShippingDateSelected)
                    {
                        row.VerifiedShippingDate = shipping.Date;
                        row.ShippingDateStatus = string.IsNullOrWhiteSpace(row.VerifiedShippingDate) ? "No match" : "Review";
                    }

                    if (OutputShippingAddressSelected)
                    {
                        row.VerifiedShippingAddress = shipping.Address;
                        row.ShippingAddressStatus = string.IsNullOrWhiteSpace(row.VerifiedShippingAddress) ? "No match" : "Review";
                    }
                }

                if (OutputAmountSelected)
                {
                    row.VerifiedAmount = ExtractAmountCandidate(ocrText, row.OriginalTransactionAmount);
                    row.AmountStatus = IsAmountMatch(row) ? "Match" : "Review";
                }

                row.RowNote = string.Empty;
                ApplyCellHighlights(row);
                ApplyRowHighlight(row);

                completedSteps += 1;
                UpdateGenerationProgress(completedSteps, totalSteps, "Row verification");
            }

            GenerationProgressPercent = 100;
            GenerationProgressText = "Generation completed (100%).";
        }
        catch (Exception ex)
        {
            GenerateError = ex.Message;
            GenerationProgressText = "Generation failed.";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private void UpdateGenerationProgress(int completed, int total, string phase)
    {
        var safeTotal = Math.Max(1, total);
        var percent = Math.Clamp((double)completed * 100d / safeTotal, 0, 100);
        GenerationProgressPercent = percent;
        GenerationProgressText = $"{phase}: {completed}/{safeTotal} ({percent:0}%)";
    }

    private static void ResetComputedOutputs(SampleRowItem row)
    {
        row.VerifiedDocumentId = string.Empty;
        row.VerifiedCustomerName = string.Empty;
        row.VerifiedAmount = null;
        row.VerifiedDocumentDate = string.Empty;
        row.VerifiedShippingDate = string.Empty;
        row.VerifiedShippingAddress = string.Empty;
        row.DocumentIdStatus = string.Empty;
        row.CustomerNameStatus = string.Empty;
        row.AmountStatus = string.Empty;
        row.DocumentDateStatus = string.Empty;
        row.ShippingDateStatus = string.Empty;
        row.ShippingAddressStatus = string.Empty;
        row.VerifiedDocumentIdCellBackground = null;
        row.VerifiedCustomerNameCellBackground = null;
        row.VerifiedAmountCellBackground = null;
        row.VerifiedDocumentDateCellBackground = null;
        row.VerifiedShippingDateCellBackground = null;
        row.VerifiedShippingAddressCellBackground = null;
    }

    private void ApplyCellHighlights(SampleRowItem row)
    {
        row.VerifiedDocumentIdCellBackground = string.Equals(row.DocumentIdStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
        row.VerifiedCustomerNameCellBackground = string.Equals(row.CustomerNameStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
        row.VerifiedAmountCellBackground = string.Equals(row.AmountStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
        row.VerifiedDocumentDateCellBackground = string.Equals(row.DocumentDateStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
        row.VerifiedShippingDateCellBackground = string.Equals(row.ShippingDateStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
        row.VerifiedShippingAddressCellBackground = string.Equals(row.ShippingAddressStatus, "Match", StringComparison.OrdinalIgnoreCase) ? MatchCellBrush : null;
    }

    private void ApplyRowHighlight(SampleRowItem row)
    {
        row.RowBackground = string.Equals(row.DocumentIdStatus, "Missing", StringComparison.OrdinalIgnoreCase)
            ? MissingRowBrush
            : null;
    }

    private EvidenceDocItem? FindBestEvidence(SampleRowItem row)
    {
        var normalizedId = NormalizeForSearch(row.DocumentId);
        var rowDigits = Regex.Replace(row.DocumentId, "\\D+", string.Empty);

        EvidenceDocItem? bestDoc = null;
        var bestScore = 0;

        foreach (var doc in EvidenceDocs)
        {
            if (!_ocrTextByDocId.TryGetValue(doc.Id, out var text))
            {
                continue;
            }

            var normalizedText = NormalizeForSearch(text);
            var normalizedDocId = NormalizeForSearch(doc.DocumentId);

            var idExact = !string.IsNullOrWhiteSpace(normalizedDocId) && normalizedDocId == normalizedId;
            var idInText = !string.IsNullOrWhiteSpace(normalizedId) && normalizedText.Contains(normalizedId, StringComparison.Ordinal);
            var digitsInText = !string.IsNullOrWhiteSpace(rowDigits) && normalizedText.Contains(rowDigits, StringComparison.Ordinal);

            if (!idExact && !idInText && !digitsInText)
            {
                continue;
            }

            var score = 0;
            if (idExact) score += 12;
            if (idInText) score += 10;
            if (digitsInText) score += 4;

            var docType = DetectDocumentTypeForDoc(doc, text);
            if (docType is DocType.Bol or DocType.Po or DocType.Je or DocType.Tb)
            {
                score -= 6;
            }

            var customerNormalized = NormalizeForSearch(row.CustomerName);
            if (!string.IsNullOrWhiteSpace(customerNormalized) && normalizedText.Contains(customerNormalized, StringComparison.Ordinal))
            {
                score += 2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestDoc = doc;
            }
        }

        return bestDoc;
    }

    private EvidenceDocItem? FindRelatedShippingDoc(string invoiceDocId, string invoiceText, string rowDocumentId)
    {
        var refs = ExtractReferenceIds(invoiceText);
        var normalizedRowDocId = NormalizeForSearch(rowDocumentId);
        if (refs.Count == 0 && string.IsNullOrWhiteSpace(normalizedRowDocId))
        {
            return null;
        }

        EvidenceDocItem? bestDoc = null;
        var bestScore = 0;

        foreach (var doc in EvidenceDocs)
        {
            if (doc.Id == invoiceDocId)
            {
                continue;
            }

            if (!_ocrTextByDocId.TryGetValue(doc.Id, out var text))
            {
                continue;
            }

            var docType = DetectDocumentTypeForDoc(doc, text);
            if (docType is not (DocType.Bol or DocType.Po or DocType.Je or DocType.Tb))
            {
                continue;
            }

            var textNorm = NormalizeForSearch(text);
            var nameNorm = NormalizeForSearch(doc.Name);
            var docIdNorm = NormalizeForSearch(doc.DocumentId);

            var score = 0;
            if (!string.IsNullOrWhiteSpace(normalizedRowDocId))
            {
                if (nameNorm.Contains(normalizedRowDocId, StringComparison.Ordinal)) score += 9;
                if (docIdNorm.Contains(normalizedRowDocId, StringComparison.Ordinal)) score += 9;
                if (textNorm.Contains(normalizedRowDocId, StringComparison.Ordinal)) score += 4;
            }

            foreach (var reference in refs)
            {
                if (textNorm.Contains(reference, StringComparison.Ordinal) || docIdNorm.Contains(reference, StringComparison.Ordinal))
                {
                    score += 8;
                }
            }

            if (Regex.IsMatch(nameNorm, "(purchaseorder|po|billoflading|bol|shipping|delivery)", RegexOptions.IgnoreCase)) score += 2;
            if (Regex.IsMatch(text, "(purchase\\s*order|\\bpo\\b|bill\\s*of\\s*lading|\\bb/?l\\b|shipping|ship\\s*to|delivery)", RegexOptions.IgnoreCase)) score += 2;

            if (score > bestScore)
            {
                bestScore = score;
                bestDoc = doc;
            }
        }

        return bestScore >= 8 ? bestDoc : null;
    }

    private static string ReadCell(DataRow row, int index)
    {
        if (index < 0 || index >= row.Table.Columns.Count)
        {
            return string.Empty;
        }

        return row[index]?.ToString()?.Trim() ?? string.Empty;
    }

    private static double ParseAmount(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "[^0-9.\\-]", string.Empty);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static string TryExtractDocumentIdFromFilename(string fileName)
    {
        var match = Regex.Match(fileName, "\\b(inv)[\\s_-]?(\\d{3,})\\b", RegexOptions.IgnoreCase);
        return match.Success ? $"INV-{match.Groups[2].Value}" : string.Empty;
    }

    private static string NormalizeForSearch(string value)
    {
        return Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
    }

    private static ScoredCandidate<string>? ExtractCustomerCandidate(string text, string expectedName)
    {
        var lines = SplitLines(text).ToList();
        var candidates = new List<ScoredCandidate<string>>();
        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            if (!Regex.IsMatch(line, "(bill\\s*to|customer(?:\\s*name)?|sold\\s*to)", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var sameLine = Regex.Replace(line, ".*?(bill\\s*to|customer(?:\\s*name)?|sold\\s*to)\\s*[:\\-]?\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            var cleanedSame = CleanExtractedCustomerName(sameLine);
            if (!string.IsNullOrWhiteSpace(cleanedSame))
            {
                candidates.Add(new ScoredCandidate<string>(cleanedSame, ScoreCustomerName(cleanedSame, true, expectedName)));
            }

            var next = i + 1 < lines.Count ? lines[i + 1] : string.Empty;
            var cleanedNext = CleanExtractedCustomerName(next);
            if (!string.IsNullOrWhiteSpace(cleanedNext))
            {
                candidates.Add(new ScoredCandidate<string>(cleanedNext, ScoreCustomerName(cleanedNext, true, expectedName)));
            }

            var tail = ExtractTailNameFromMixedLine(next);
            if (!string.IsNullOrWhiteSpace(tail))
            {
                candidates.Add(new ScoredCandidate<string>(tail, ScoreCustomerName(tail, true, expectedName) + 1));
            }
        }

        foreach (var pattern in new[]
                 {
                     @"bill\s*to\s*[:\-]?\s*([^\n\r]+)",
                     @"customer(?:\s*name)?\s*[:\-]?\s*([^\n\r]+)",
                     @"sold\s*to\s*[:\-]?\s*([^\n\r]+)",
                 })
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            var cleaned = CleanExtractedCustomerName(m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                candidates.Add(new ScoredCandidate<string>(cleaned, ScoreCustomerName(cleaned, false, expectedName)));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.OrderByDescending(x => x.Score).First();
        return best.Score >= 3 ? best : null;
    }

    private static ScoredCandidate<string>? ExtractDocumentDateCandidate(string text, string expectedDate)
    {
        var lines = SplitLines(text).ToList();
        var expectedIso = ToIsoDate(expectedDate);
        var candidates = new List<ScoredCandidate<string>>();

        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            var lineDates = Regex.Matches(line, "(\\d{1,4}[/-]\\d{1,2}[/-]\\d{2,4})")
                .Cast<Match>()
                .Select(x => NormalizeDateToken(x.Groups[1].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var dateValue in lineDates)
            {
                var score = 1;
                var lower = line.ToLowerInvariant();
                var prev = i > 0 ? lines[i - 1].ToLowerInvariant() : string.Empty;
                var next = i + 1 < lines.Count ? lines[i + 1].ToLowerInvariant() : string.Empty;

                if (Regex.IsMatch(lower, "invoice\\s*date|date\\s*issued|document\\s*date")) score += 4;
                else if (Regex.IsMatch(lower, "invoice|issued")) score += 2;
                if (Regex.IsMatch(prev, "invoice")) score += 2;
                if (Regex.IsMatch(lower, "due\\s*date") || Regex.IsMatch(prev, "due\\s*date") || Regex.IsMatch(next, "due\\s*date")) score -= 2;

                if (i < Math.Max(1, lines.Count / 3)) score += 1;

                var candidateIso = ToIsoDate(dateValue);
                if (!string.IsNullOrWhiteSpace(expectedIso) && !string.IsNullOrWhiteSpace(candidateIso) && candidateIso == expectedIso)
                {
                    score += 2;
                }

                candidates.Add(new ScoredCandidate<string>(dateValue, score));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.OrderByDescending(x => x.Score).First();
        return best.Score >= 3 ? best : null;
    }

    private static string CleanExtractedCustomerName(string value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return string.Empty;

        candidate = Regex.Split(candidate, "\\b(phone|fax|e-?mail|address|invoice|bill\\s*to|ship\\s*to|contact)\\b", RegexOptions.IgnoreCase)[0].Trim();
        candidate = Regex.Replace(candidate, "\\s+\\d{1,2}[/-]\\d{1,2}[/-]\\d{2,4}\\s*$", string.Empty).Trim();
        candidate = Regex.Replace(candidate, "^[^a-z0-9]+|[^a-z0-9]+$", string.Empty, RegexOptions.IgnoreCase).Trim();
        var letters = Regex.Matches(candidate, "[a-z]", RegexOptions.IgnoreCase).Count;
        return letters >= 3 ? candidate : string.Empty;
    }

    private static int ScoreCustomerName(string value, bool fromLabelLine, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(value)) return -10;

        var score = fromLabelLine ? 2 : 0;
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var alphaTokens = tokens.Where(t => Regex.IsMatch(t, "^[a-z][a-z'.-]*$", RegexOptions.IgnoreCase)).ToList();

        if (tokens.Length >= 2 && tokens.Length <= 4) score += 2;
        if (alphaTokens.Count >= 2) score += 2;
        if (tokens.Length == 1 && alphaTokens.Count == 1 && value.Length >= 4) score += 1;
        if (Regex.IsMatch(value, "\\b(invoice|total|amount|description|qty|quantity|price|date|tax|email|phone|fax|street|city|state|zip)\\b", RegexOptions.IgnoreCase)) score -= 3;
        if (Regex.IsMatch(value, "\\d")) score -= 2;
        if (tokens.Any(t => t.Length <= 1)) score -= 1;

        if (!string.IsNullOrWhiteSpace(expectedName))
        {
            var similarity = TokenSimilarity(value, expectedName);
            if (similarity >= 0.75) score += 2;
            else if (similarity >= 0.5) score += 1;
        }

        return score;
    }

    private static string ExtractTailNameFromMixedLine(string line)
    {
        var cleaned = Regex.Replace(line ?? string.Empty, "[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "\\+?\\d[\\d\\s().-]{6,}", " ");
        cleaned = Regex.Replace(cleaned, "\\d{1,4}[/-]\\d{1,2}[/-]\\d{2,4}", " ");
        cleaned = Regex.Replace(cleaned, "[^A-Za-z\\s'-]+", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

        var match = Regex.Match(cleaned, "([A-Za-z][A-Za-z'-]{2,}(?:\\s+[A-Za-z][A-Za-z'-]{2,}){0,2})\\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static (string Date, string Address) ExtractShippingFieldsFromOcr(string text)
    {
        var lines = SplitLines(text).ToList();
        var docType = DetectDocumentType(text);

        var generic = ExtractShippingGeneric(lines);
        var specific = docType switch
        {
            DocType.Bol => ExtractShippingFromBol(lines),
            DocType.Po => ExtractShippingFromPo(lines),
            DocType.Je => ExtractShippingFromJe(lines),
            DocType.Tb => ExtractShippingFromTb(lines),
            _ => (Date: string.Empty, Address: string.Empty),
        };

        var date = !string.IsNullOrWhiteSpace(specific.Date) ? specific.Date : generic.Date;
        var address = !string.IsNullOrWhiteSpace(specific.Address) ? specific.Address : generic.Address;
        address = CleanShippingAddress(address);
        return (date, address);
    }

    private static (string Date, string Address) ExtractShippingGeneric(IReadOnlyList<string> lines)
    {
        var date = string.Empty;
        var address = string.Empty;

        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            if (!Regex.IsMatch(line, "(ship\\s*date|shipping\\s*date|delivery\\s*date|dispatch\\s*date)", RegexOptions.IgnoreCase))
            {
                continue;
            }

            date = FirstDateToken(line);
            if (!string.IsNullOrWhiteSpace(date)) break;

            if (i + 1 < lines.Count)
            {
                date = FirstDateToken(lines[i + 1]);
                if (!string.IsNullOrWhiteSpace(date)) break;
            }
        }

        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            var inline = Regex.Match(line, "(ship\\s*to|shipping\\s*address|delivery\\s*address|deliver\\s*to)\\s*[:\\-]?\\s*(.+)", RegexOptions.IgnoreCase);
            if (inline.Success)
            {
                var value = inline.Groups[2].Value.Trim();
                if (IsLikelyAddressFragment(value))
                {
                    address = value;
                    break;
                }
            }

            if (!Regex.IsMatch(line, "^(ship\\s*to|shipping\\s*address|delivery\\s*address|deliver\\s*to)\\s*[:\\-]?$", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var collected = new List<string>();
            for (var j = i + 1; j < Math.Min(lines.Count, i + 4); j += 1)
            {
                var next = lines[j].Trim();
                if (string.IsNullOrWhiteSpace(next)) continue;
                if (Regex.IsMatch(next, "(invoice|date|total|amount|description|qty|quantity|price|phone|fax|email|contact)", RegexOptions.IgnoreCase)) break;
                if (IsLikelyAddressFragment(next))
                {
                    collected.Add(next);
                }
            }

            if (collected.Count > 0)
            {
                address = string.Join(", ", collected);
                break;
            }
        }

        return (date, address);
    }

    private static (string Date, string Address) ExtractShippingFromBol(IReadOnlyList<string> lines)
    {
        var generic = ExtractShippingGeneric(lines);
        var shipToStart = lines
            .Select((line, index) => new { line, index })
            .FirstOrDefault(x => Regex.IsMatch(x.line, "\\bship\\s*to\\b", RegexOptions.IgnoreCase))?.index ?? -1;
        if (shipToStart < 0)
        {
            return generic;
        }

        var shipToEnd = lines
            .Select((line, index) => new { line, index })
            .FirstOrDefault(x => x.index > shipToStart && Regex.IsMatch(x.line, "(third\\s*party\\s*freight|special\\s*instructions|customer\\s*order\\s*no|handling\\s*unit)", RegexOptions.IgnoreCase))?.index ?? -1;

        var take = shipToEnd > shipToStart ? shipToEnd - shipToStart : Math.Min(14, lines.Count - shipToStart);
        var shipBlock = lines.Skip(shipToStart).Take(Math.Max(0, take)).ToList();
        var contacts = ExtractShipToContacts(shipBlock);
        var shipAddress = ExtractLabeledFieldFromLines(shipBlock, new[] { "address", "street" }, new[] { "serial", "cid no", "carrier name" });
        var shipCity = ExtractLabeledFieldFromLines(shipBlock, new[] { "city / state / zip", "city/state/zip", "city state zip" }, new[] { "cid no", "carrier name" });
        var addressParts = contacts.Concat(new[] { shipAddress, shipCity })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(IsLikelyAddressFragment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var address = string.Join(", ", addressParts);
        if (string.IsNullOrWhiteSpace(address))
        {
            address = generic.Address;
        }

        var dateCandidates = CollectDateCandidates(lines, new[]
        {
            "ship\\s*date",
            "shipping\\s*date",
            "pick-?up\\s*date",
            "shipper\\s*signature\\s*&\\s*date",
        });

        var bolDate = generic.Date;
        if (dateCandidates.Count > 0)
        {
            bolDate = dateCandidates
                .OrderBy(d => ToComparableDate(d) ?? long.MaxValue)
                .FirstOrDefault() ?? generic.Date;
        }

        return (bolDate, address);
    }

    private static (string Date, string Address) ExtractShippingFromPo(IReadOnlyList<string> lines)
    {
        var generic = ExtractShippingGeneric(lines);
        var shipToStart = lines
            .Select((line, index) => new { line, index })
            .FirstOrDefault(x => Regex.IsMatch(x.line, "\\bship\\s*to\\b", RegexOptions.IgnoreCase))?.index ?? -1;
        if (shipToStart < 0)
        {
            return generic;
        }

        var shipToEnd = lines
            .Select((line, index) => new { line, index })
            .FirstOrDefault(x => x.index > shipToStart && Regex.IsMatch(x.line, "(bill\\s*to|vendor|supplier|payment\\s*terms|terms|item\\s*description|line\\s*items?|description\\s*qty)", RegexOptions.IgnoreCase))?.index ?? -1;

        var take = shipToEnd > shipToStart ? shipToEnd - shipToStart : Math.Min(16, lines.Count - shipToStart);
        var shipBlock = lines.Skip(shipToStart).Take(Math.Max(0, take)).ToList();
        var contacts = ExtractShipToContacts(shipBlock);
        var shipName = ExtractLabeledFieldFromLines(shipBlock, new[] { "name", "ship to", "consignee" }, new[] { "phone", "email", "fax", "city" });
        var shipAddress = ExtractLabeledFieldFromLines(shipBlock, new[] { "address", "street" }, new[] { "phone", "email", "fax" });
        var shipCity = ExtractLabeledFieldFromLines(shipBlock, new[] { "city / state / zip", "city/state/zip", "city, state, zip", "city state zip" }, new[] { "phone", "email", "fax" });
        var addressParts = contacts.Concat(new[] { shipName, shipAddress, shipCity })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(IsLikelyAddressFragment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var address = addressParts.Count > 0 ? string.Join(", ", addressParts) : generic.Address;

        var dateCandidates = CollectDateCandidates(lines, new[] { "ship\\s*date", "delivery\\s*date", "requested\\s*date", "promised\\s*date", "order\\s*date" });
        var date = dateCandidates.Count > 0 ? dateCandidates[0] : generic.Date;
        return (date, address);
    }

    private static (string Date, string Address) ExtractShippingFromJe(IReadOnlyList<string> lines)
    {
        return ExtractShippingGeneric(lines);
    }

    private static (string Date, string Address) ExtractShippingFromTb(IReadOnlyList<string> lines)
    {
        var text = string.Join("\n", lines);
        var hasShippingCue = Regex.IsMatch(text, "(ship\\s*to|shipping\\s*address|shipping\\s*date|delivery\\s*date|bill\\s*of\\s*lading|\\bbol\\b)", RegexOptions.IgnoreCase);
        return hasShippingCue ? ExtractShippingGeneric(lines) : (string.Empty, string.Empty);
    }

    private static List<string> ExtractShipToContacts(IReadOnlyList<string> lines)
    {
        var contacts = new List<string>();
        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i].Trim();
            if (!Regex.IsMatch(line, "\\bname\\b", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(line, "(carrier\\s*name|third\\s*party|shipper|vendor|supplier)", RegexOptions.IgnoreCase)) continue;

            var value = Regex.Replace(line, ".*?\\bname\\b\\s*[:\\-]?\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(value) && i + 1 < lines.Count)
            {
                value = lines[i + 1].Trim();
            }

            if (!IsLikelyAddressFragment(value)) continue;
            if (Regex.IsMatch(value, "(carrier\\s*name|trailer|serial|cid\\s*no|sid\\s*no|city\\s*/\\s*state\\s*/\\s*zip|vendor|supplier)", RegexOptions.IgnoreCase)) continue;
            contacts.Add(value);
        }

        return contacts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExtractLabeledFieldFromLines(IReadOnlyList<string> lines, IReadOnlyList<string> labels, IReadOnlyList<string> stopMarkers)
    {
        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            if (!labels.Any(label => line.Contains(label, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var extracted = line;
            foreach (var label in labels)
            {
                extracted = Regex.Replace(extracted, $".*?{Regex.Escape(label)}\\s*[:\\-]?\\s*", string.Empty, RegexOptions.IgnoreCase);
            }

            extracted = extracted.Trim();
            foreach (var marker in stopMarkers)
            {
                var idx = extracted.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    extracted = extracted[..idx].Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(extracted) && !IsHeaderLikeText(extracted))
            {
                return extracted;
            }

            var next = i + 1 < lines.Count ? lines[i + 1] : string.Empty;
            if (!string.IsNullOrWhiteSpace(next) && !IsHeaderLikeText(next))
            {
                return next.Trim();
            }
        }

        return string.Empty;
    }

    private static List<string> CollectDateCandidates(IReadOnlyList<string> lines, IReadOnlyList<string> anchorRegexes)
    {
        var found = new List<string>();
        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            if (!anchorRegexes.Any(pattern => Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase)))
            {
                continue;
            }

            var window = new[]
            {
                i > 0 ? lines[i - 1] : string.Empty,
                line,
                i + 1 < lines.Count ? lines[i + 1] : string.Empty,
                i + 2 < lines.Count ? lines[i + 2] : string.Empty,
            };

            foreach (var part in window)
            {
                foreach (Match match in Regex.Matches(part, "(\\d{1,4}[/-]\\d{1,2}[/-]\\d{2,4})"))
                {
                    if (match.Groups.Count > 1)
                    {
                        var normalized = NormalizeDateToken(match.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(normalized) && !found.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(normalized);
                        }
                    }
                }
            }
        }

        // Fallback for OCR that places dates as standalone lines with weak/no labels.
        if (found.Count == 0)
        {
            foreach (var line in lines)
            {
                var m = Regex.Match(line, "^(\\d{1,4}[/-]\\d{1,2}[/-]\\d{2,4})$");
                if (!m.Success || m.Groups.Count < 2)
                {
                    continue;
                }

                var normalized = NormalizeDateToken(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(normalized) && !found.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(normalized);
                }
            }
        }

        return found;
    }

    private static long? ToComparableDate(string value)
    {
        var iso = ToIsoDate(value);
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        return DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Ticks
            : null;
    }

    private static bool IsHeaderLikeText(string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v)) return true;
        if (Regex.IsMatch(v, "^[A-Z\\s/&.-]+$") && v.Length > 8) return true;
        if (Regex.IsMatch(v, "(carrier\\s*name|description\\s*of\\s*articles|special\\s*marks|exceptions|nmfc|class|qty|type|line\\s*total)", RegexOptions.IgnoreCase)) return true;
        return false;
    }

    private static bool IsLikelyAddressFragment(string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v)) return false;
        if (Regex.IsMatch(v, "(carrier\\s*name|trailer\\s*no|serial\\s*nos?|cid\\s*no|sid\\s*no)", RegexOptions.IgnoreCase)) return false;
        var letters = Regex.Matches(v, "[a-z]", RegexOptions.IgnoreCase).Count;
        return letters >= 4;
    }

    private static string CleanShippingAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var parts = value.Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !Regex.IsMatch(x, "(^hwy\\s*carrier$|carrier\\s*name|trailer\\s*no|serial\\s*nos?|cid\\s*no|sid\\s*no)", RegexOptions.IgnoreCase))
            .ToList();

        return Regex.Replace(string.Join(", ", parts), "\\s+", " ").Trim();
    }

    private static DocType DetectDocumentTypeForDoc(EvidenceDocItem doc, string text)
    {
        var metadata = $"{doc.Name} {doc.DocumentId}";
        if (Regex.IsMatch(metadata, "bill\\s*of\\s*lading|\\bbol\\b|\\bb/?l\\b", RegexOptions.IgnoreCase)) return DocType.Bol;
        if (Regex.IsMatch(metadata, "purchase\\s*order|\\bpo\\b", RegexOptions.IgnoreCase)) return DocType.Po;
        if (Regex.IsMatch(metadata, "journal\\s*entry|journal\\s*id|journal\\s*no|\\bje\\b", RegexOptions.IgnoreCase)) return DocType.Je;
        if (Regex.IsMatch(metadata, "trial\\s*balance|\\btb\\b", RegexOptions.IgnoreCase)) return DocType.Tb;
        return DetectDocumentType(text);
    }

    private static DocType DetectDocumentType(string text)
    {
        if (Regex.IsMatch(text, "bill\\s*of\\s*lading|\\bbol\\b|\\bb/?l\\b|shipper|ship\\s*to", RegexOptions.IgnoreCase)) return DocType.Bol;
        if (Regex.IsMatch(text, "purchase\\s*order|\\bpo\\b", RegexOptions.IgnoreCase)) return DocType.Po;
        if (Regex.IsMatch(text, "journal\\s*entry|journal\\s*id|journal\\s*no|journal\\s*number|\\bje\\b", RegexOptions.IgnoreCase)) return DocType.Je;
        if (Regex.IsMatch(text, "trial\\s*balance|\\btb\\b", RegexOptions.IgnoreCase)) return DocType.Tb;
        if (Regex.IsMatch(text, "invoice", RegexOptions.IgnoreCase)) return DocType.Invoice;
        return DocType.Other;
    }

    private static HashSet<string> ExtractReferenceIds(string text)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new[]
        {
            @"purchase\s*order\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\bpo\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"bill\s*of\s*lading\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\bb/?l\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\bbol\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\bje\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\btb\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})",
            @"\b([A-Z]{1,4}-\d{4,})\b",
        };

        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(text ?? string.Empty, pattern, RegexOptions.IgnoreCase))
            {
                if (match.Groups.Count < 2) continue;
                var normalized = NormalizeForSearch(match.Groups[1].Value);
                if (normalized.Length >= 5)
                {
                    refs.Add(normalized);
                }
            }
        }

        return refs;
    }

    private static double TokenSimilarity(string left, string right)
    {
        var leftTokens = left
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ')
            .ToArray();
        var rightTokens = right
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ')
            .ToArray();

        var leftSet = new HashSet<string>(new string(leftTokens)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 1), StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(new string(rightTokens)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 1), StringComparer.OrdinalIgnoreCase);

        if (leftSet.Count == 0 || rightSet.Count == 0)
        {
            return 0;
        }

        var intersection = leftSet.Count(token => rightSet.Contains(token));
        var union = leftSet.Count + rightSet.Count - intersection;
        return union > 0 ? (double)intersection / union : 0;
    }

    private static double? ExtractAmountCandidate(string text, double expectedAmount)
    {
        var lines = SplitLines(text).ToList();
        var candidates = new List<ScoredCandidate<double>>();

        for (var i = 0; i < lines.Count; i += 1)
        {
            var line = lines[i];
            var lower = line.ToLowerInvariant();
            var prev = i > 0 ? lines[i - 1].ToLowerInvariant() : string.Empty;
            var next = i + 1 < lines.Count ? lines[i + 1].ToLowerInvariant() : string.Empty;
            var prev2 = i > 1 ? lines[i - 2].ToLowerInvariant() : string.Empty;
            var next2 = i + 2 < lines.Count ? lines[i + 2].ToLowerInvariant() : string.Empty;

            var isGrandTotalContext =
                Regex.IsMatch(lower, "(^|\\b)(grand\\s*)?total\\b|total\\s*due|amount\\s*due|invoice\\s*total") &&
                !Regex.IsMatch(lower, "line\\s*total");
            var isLineItemContext = Regex.IsMatch(lower, "line\\s*total|unit\\s*price|quantity|description|rate\\s*per\\s*hour|hours|discount");
            var isSubtotalLine = Regex.IsMatch(lower, "\\bsubtotal\\b");
            var taxAndTotalBlockNearby =
                Regex.IsMatch(lower, "sales\\s*tax\\s*total") ||
                Regex.IsMatch(prev, "sales\\s*tax\\s*total") ||
                Regex.IsMatch(next, "sales\\s*tax\\s*total") ||
                Regex.IsMatch(prev2, "sales\\s*tax\\s*total") ||
                Regex.IsMatch(next2, "sales\\s*tax\\s*total") ||
                Regex.IsMatch(prev, "sales\\s*tax") ||
                Regex.IsMatch(next, "sales\\s*tax") ||
                Regex.IsMatch(prev2, "sales\\s*tax") ||
                Regex.IsMatch(next2, "sales\\s*tax");

            var lineAmounts = Regex.Matches(line, @"\$?\s*([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})|[0-9]+\.[0-9]{2})")
                .Cast<Match>()
                .Select(m => new { Raw = m.Groups[1].Value, Value = ParseAmountToken(m.Groups[1].Value) })
                .Where(m => m.Value.HasValue)
                .Select(m => new { m.Raw, Value = m.Value!.Value })
                .ToList();

            var amountCount = lineAmounts.Count;
            for (var j = 0; j < lineAmounts.Count; j += 1)
            {
                var amount = lineAmounts[j];
                var score = 1;

                if (isGrandTotalContext) score += 7;
                else if (Regex.IsMatch(lower, "\\btotal\\b")) score += 2;

                if (isLineItemContext) score -= 4;
                if (isSubtotalLine) score -= 1;
                if (taxAndTotalBlockNearby) score += 2;
                if (line.Contains('$')) score += 1;
                if (amount.Value >= 100) score += 1;
                if (amountCount >= 3 && line.Contains('$')) score += 2;

                if (amountCount > 1 && (isGrandTotalContext || taxAndTotalBlockNearby))
                {
                    if (j == amountCount - 1) score += 2;
                    else score -= 1;
                }

                if (amountCount > 1 && !isGrandTotalContext && !taxAndTotalBlockNearby)
                {
                    score -= 2;
                }

                var delta = Math.Abs(amount.Value - expectedAmount);
                if (delta <= 0.01) score += 3;
                else if (delta <= Math.Max(1, expectedAmount * 0.05)) score += 2;

                candidates.Add(new ScoredCandidate<double>(amount.Value, score));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Value)
            .First();

        return best.Score >= 4 ? best.Value : null;
    }

    private static double? ParseAmountToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = value.Replace(",", string.Empty).Trim();
        return double.TryParse(parsed, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? Math.Round(result, 2)
            : null;
    }

    private static string NormalizeDateToken(string value)
    {
        var token = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var anchored = Regex.Match(token, "^(\\d{1,4}[/-]\\d{1,2}[/-]\\d{2,4})");
        return anchored.Success ? anchored.Groups[1].Value : string.Empty;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return (text ?? string.Empty)
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0);
    }

    private static string FirstDateToken(string text)
    {
        var match = Regex.Match(text, @"\b(\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4})\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static bool IsDocumentIdMatch(SampleRowItem row)
    {
        var left = NormalizeForSearch(row.DocumentId);
        var right = NormalizeForSearch(row.VerifiedDocumentId);
        return !string.IsNullOrWhiteSpace(left) && left == right;
    }

    private static bool IsCustomerNameMatch(SampleRowItem row)
    {
        var left = NormalizeForSearch(row.CustomerName);
        var right = NormalizeForSearch(row.VerifiedCustomerName);
        return !string.IsNullOrWhiteSpace(left) && left == right;
    }

    private static bool IsDateMatch(SampleRowItem row)
    {
        var leftIso = ToIsoDate(row.TransactionDate);
        var rightIso = ToIsoDate(row.VerifiedDocumentDate);

        if (!string.IsNullOrWhiteSpace(leftIso) && !string.IsNullOrWhiteSpace(rightIso))
        {
            return leftIso == rightIso;
        }

        return false;
    }

    private static string NormalizeDate(string value)
    {
        return ToIsoDate(value) ?? string.Empty;
    }

    private static string? ToIsoDate(string value)
    {
        var v = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }

        var ymd = Regex.Match(v, "^(\\d{4})-(\\d{2})-(\\d{2})$");
        if (ymd.Success)
        {
            return $"{ymd.Groups[1].Value}-{ymd.Groups[2].Value}-{ymd.Groups[3].Value}";
        }

        var mdySlash = Regex.Match(v, "^(\\d{1,2})/(\\d{1,2})/(\\d{2,4})$");
        if (mdySlash.Success)
        {
            var mm = mdySlash.Groups[1].Value.PadLeft(2, '0');
            var dd = mdySlash.Groups[2].Value.PadLeft(2, '0');
            var yyyy = mdySlash.Groups[3].Value.Length == 2 ? $"20{mdySlash.Groups[3].Value}" : mdySlash.Groups[3].Value;
            return $"{yyyy}-{mm}-{dd}";
        }

        var mdyDash = Regex.Match(v, "^(\\d{1,2})-(\\d{1,2})-(\\d{2,4})$");
        if (mdyDash.Success)
        {
            var mm = mdyDash.Groups[1].Value.PadLeft(2, '0');
            var dd = mdyDash.Groups[2].Value.PadLeft(2, '0');
            var yyyy = mdyDash.Groups[3].Value.Length == 2 ? $"20{mdyDash.Groups[3].Value}" : mdyDash.Groups[3].Value;
            return $"{yyyy}-{mm}-{dd}";
        }

        return null;
    }

    private static bool IsAmountMatch(SampleRowItem row)
    {
        if (!row.VerifiedAmount.HasValue)
        {
            return false;
        }

        var left = Math.Abs(Math.Round(row.OriginalTransactionAmount, 2));
        var right = Math.Abs(Math.Round(row.VerifiedAmount.Value, 2));
        return left == right;
    }

    private DataTable? _pendingDataTable;

    private static DataTable BuildTableFromRawRows(List<List<string>> rows)
    {
        var normalizedRows = rows
            .Where(x => x.Any(y => !string.IsNullOrWhiteSpace(y)))
            .ToList();

        if (normalizedRows.Count < 2)
        {
            throw new InvalidOperationException("Sample file must include a header row and at least one data row.");
        }

        var headerIndex = FindBestHeaderRowIndex(normalizedRows);
        var headerRow = normalizedRows[headerIndex];
        var dataRows = normalizedRows.Skip(headerIndex + 1).ToList();

        var table = new DataTable();
        var usedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var col = 0; col < headerRow.Count; col += 1)
        {
            var rawHeader = headerRow[col];
            var baseHeader = string.IsNullOrWhiteSpace(rawHeader) ? $"Column {col + 1}" : rawHeader.Trim();
            var uniqueHeader = baseHeader;
            var suffix = 2;

            while (!usedHeaders.Add(uniqueHeader))
            {
                uniqueHeader = $"{baseHeader} ({suffix})";
                suffix += 1;
            }

            table.Columns.Add(uniqueHeader);
        }

        foreach (var sourceRow in dataRows)
        {
            var row = table.NewRow();
            for (var col = 0; col < table.Columns.Count; col += 1)
            {
                row[col] = col < sourceRow.Count ? sourceRow[col] : string.Empty;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static int FindBestHeaderRowIndex(List<List<string>> rows)
    {
        var aliases = new[]
        {
            new[] { "documentid", "invoiceid", "docid", "document" },
            new[] { "customername", "customer", "client", "billto", "soldto" },
            new[] { "transactiondate", "documentdate", "invoicedate", "date" },
            new[] { "originaltransactionamount", "amount", "invoiceamount", "total", "transactionamount" },
        };

        var bestIndex = 0;
        var bestScore = int.MinValue;
        var maxRows = Math.Min(rows.Count, 10);

        for (var i = 0; i < maxRows; i += 1)
        {
            var score = 0;
            var normalizedCells = rows[i]
                .Select(NormalizeLabel)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var group in aliases)
            {
                if (normalizedCells.Any(cell => group.Any(alias => cell == alias || cell.Contains(alias, StringComparison.Ordinal))))
                {
                    score += 4;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestScore > 0 ? bestIndex : 0;
    }

    private DataTable CacheAndReturn(DataTable table)
    {
        _pendingDataTable = table;
        return table;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    public DataTable PreparePendingTable(DataTable table) => CacheAndReturn(table);
}
