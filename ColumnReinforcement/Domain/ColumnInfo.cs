using Autodesk.Revit.DB;

namespace ColumnReinforcement.Domain;

/// <summary>
/// Lightweight snapshot of a Revit column for the dialog's validation table.
/// Pulled from <see cref="ColumnGeometry"/> + the Mark parameter in the command,
/// so the dialog itself doesn't need access to the Revit Document.
/// </summary>
public record ColumnInfo(
    ElementId Id,
    string? Mark,
    ColumnSection Section,
    double WidthIn,
    double DepthIn);
