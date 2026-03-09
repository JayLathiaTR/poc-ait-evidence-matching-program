using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AitEvidenceMatching.App.ViewModels;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace AitEvidenceMatching.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyOutputColumnVisibility(_viewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedDocumentIdColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedCustomerNameColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedDocumentDateColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedAmountColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedShippingDateColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowVerifiedShippingAddressColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowDocumentIdStatusColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowCustomerNameStatusColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowDocumentDateStatusColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowAmountStatusColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowShippingDateStatusColumn) ||
            e.PropertyName == nameof(MainWindowViewModel.ShowShippingAddressStatusColumn))
        {
            ApplyOutputColumnVisibility(_viewModel);
        }
    }

    private void ApplyOutputColumnVisibility(MainWindowViewModel vm)
    {
        if (SampleRowsGrid.Columns.Count < 10)
        {
            return;
        }

        // Output value columns.
        SampleRowsGrid.Columns[4].IsVisible = vm.ShowVerifiedDocumentIdColumn;
        SampleRowsGrid.Columns[5].IsVisible = vm.ShowVerifiedCustomerNameColumn;
        SampleRowsGrid.Columns[6].IsVisible = vm.ShowVerifiedAmountColumn;
        SampleRowsGrid.Columns[7].IsVisible = vm.ShowVerifiedDocumentDateColumn;
        SampleRowsGrid.Columns[8].IsVisible = vm.ShowVerifiedShippingDateColumn;
        SampleRowsGrid.Columns[9].IsVisible = vm.ShowVerifiedShippingAddressColumn;
    }

    private async void OnImportSampleClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select Sample File",
            FileTypeFilter =
            [
                new FilePickerFileType("Sample Files") { Patterns = ["*.csv", "*.xlsx", "*.xls"] },
            ],
        });

        var selected = files.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        var path = GetLocalPath(selected);
        if (string.IsNullOrWhiteSpace(path))
        {
            vm.GenerateError = "Selected sample file could not be resolved to a local path.";
            return;
        }

        try
        {
            var table = vm.ParseSampleFile(path);
            vm.PreparePendingTable(table);
            vm.SetPendingSample(table, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            vm.GenerateError = ex.Message;
        }
    }

    private async void OnAddEvidenceFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select Evidence Files",
            FileTypeFilter =
            [
                new FilePickerFileType("Documents") { Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff"] },
            ],
        });

        var localPaths = files
            .Select(GetLocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        vm.AddEvidenceFiles(localPaths);
    }

    private async void OnDownloadSampleTemplateClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Sample Template",
            SuggestedFileName = "ait-sample-template.csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv"],
                },
            ],
        });

        if (file is null)
        {
            return;
        }

        const string headers = "Document ID,Customer name,Transaction date,Original transaction amount";
        const string exampleRow = "INV-1099,Russell Proctor,2021-03-25,3000.20";
        var csv = string.Concat(headers, Environment.NewLine, exampleRow, Environment.NewLine);

        await using var stream = await file.OpenWriteAsync();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(csv);
        await writer.FlushAsync();
    }

    private static string? GetLocalPath(IStorageItem file)
    {
        if (file.Path.IsAbsoluteUri)
        {
            return Uri.UnescapeDataString(file.Path.LocalPath);
        }

        return file.Path.OriginalString;
    }
}