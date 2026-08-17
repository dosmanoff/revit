using Autodesk.Revit.DB;
using RevitActionRecorder.Model;

namespace RevitActionRecorder.Recording;

/// <summary>Извлечение краткой карточки элемента для событийного потока. Только главный поток.</summary>
internal static class ElementExtractor
{
    public static ElementBrief? Brief(Document doc, ElementId id)
    {
        var element = doc.GetElement(id);
        if (element is null)
            return new ElementBrief { Id = id.Value };

        var brief = new ElementBrief
        {
            Id = id.Value,
            Uid = element.UniqueId,
            Cat = element.Category?.Name,
            Cls = element.GetType().Name,
        };

        try
        {
            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType elementType)
            {
                brief.Family = elementType.FamilyName;
                brief.Type = elementType.Name;
            }
            else if (element is ElementType ownType)
            {
                brief.Family = ownType.FamilyName;
                brief.Type = ownType.Name;
            }
        }
        catch
        {
        }

        try
        {
            if (element.LevelId != ElementId.InvalidElementId && doc.GetElement(element.LevelId) is Level level)
                brief.Level = level.Name;
        }
        catch
        {
        }

        try
        {
            if (doc.IsWorkshared && element.WorksetId != WorksetId.InvalidWorksetId)
                brief.Workset = doc.GetWorksetTable().GetWorkset(element.WorksetId)?.Name;
        }
        catch
        {
        }

        return brief;
    }
}
