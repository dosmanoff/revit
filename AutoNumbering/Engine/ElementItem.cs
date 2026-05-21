using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AutoNumbering.Engine;

public class ElementItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isIncluded = true;
    private string _proposedNumber = string.Empty;

    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string Category { get; init; } = string.Empty;
    public string FamilyType { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = string.Empty;
    public string SortKeyValue { get; init; } = string.Empty;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value) return;
            _isIncluded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
        }
    }

    public string ProposedNumber
    {
        get => _proposedNumber;
        set
        {
            if (_proposedNumber == value) return;
            _proposedNumber = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProposedNumber)));
        }
    }
}
