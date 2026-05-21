using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SlabRebar.Engine;

public class RebarItem : INotifyPropertyChanged
{
    public ElementId Id           { get; set; } = ElementId.InvalidElementId;
    public string    HostName     { get; set; } = string.Empty;
    // "Bottom" | "Top"
    public string    Zone         { get; set; } = string.Empty;
    // "X" | "Y"
    public string    Direction    { get; set; } = string.Empty;
    public string    CurrentValue { get; set; } = string.Empty;

    private string _proposedLabel = string.Empty;
    public string ProposedLabel
    {
        get => _proposedLabel;
        set { _proposedLabel = value; OnPropertyChanged(); }
    }

    private bool _isIncluded = true;
    public bool IsIncluded
    {
        get => _isIncluded;
        set { _isIncluded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
