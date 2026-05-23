namespace RevitPlugin.Domain.Abstractions;

/// <summary>Доменное представление Revit-документа (без зависимости от Revit API).</summary>
public interface IRevitDocument
{
    string PathName { get; }
    string Title { get; }
}
