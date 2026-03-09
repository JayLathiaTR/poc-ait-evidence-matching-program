using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace AitEvidenceMatching.App.Models;

public partial class SampleRowItem : ObservableObject
{
    public string TransactionId { get; init; } = string.Empty;

    [ObservableProperty]
    private string documentId = string.Empty;

    [ObservableProperty]
    private string customerName = string.Empty;

    [ObservableProperty]
    private string transactionDate = string.Empty;

    [ObservableProperty]
    private double originalTransactionAmount;

    [ObservableProperty]
    private string linkedEvidenceName = string.Empty;

    [ObservableProperty]
    private string verifiedDocumentId = string.Empty;

    [ObservableProperty]
    private string verifiedCustomerName = string.Empty;

    [ObservableProperty]
    private string verifiedDocumentDate = string.Empty;

    [ObservableProperty]
    private double? verifiedAmount;

    [ObservableProperty]
    private string verifiedShippingDate = string.Empty;

    [ObservableProperty]
    private string verifiedShippingAddress = string.Empty;

    [ObservableProperty]
    private string documentIdStatus = string.Empty;

    [ObservableProperty]
    private string customerNameStatus = string.Empty;

    [ObservableProperty]
    private string documentDateStatus = string.Empty;

    [ObservableProperty]
    private string amountStatus = string.Empty;

    [ObservableProperty]
    private string shippingDateStatus = string.Empty;

    [ObservableProperty]
    private string shippingAddressStatus = string.Empty;

    [ObservableProperty]
    private string rowNote = string.Empty;

    [ObservableProperty]
    private IBrush? rowBackground;

    [ObservableProperty]
    private IBrush? verifiedDocumentIdCellBackground;

    [ObservableProperty]
    private IBrush? verifiedCustomerNameCellBackground;

    [ObservableProperty]
    private IBrush? verifiedAmountCellBackground;

    [ObservableProperty]
    private IBrush? verifiedDocumentDateCellBackground;

    [ObservableProperty]
    private IBrush? verifiedShippingDateCellBackground;

    [ObservableProperty]
    private IBrush? verifiedShippingAddressCellBackground;
}
