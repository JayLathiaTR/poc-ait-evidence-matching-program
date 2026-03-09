using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace AitEvidenceMatching.App.Models;

public partial class EvidenceDocItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string FilePath { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    private string documentId = string.Empty;

    [ObservableProperty]
    private bool isSelected;
}
