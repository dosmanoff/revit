using System.Globalization;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>Настройки проекта: информация, единицы, уровни/оси, стадии, воркcеты, варианты, связи, координаты, глобальные параметры, печать.</summary>
internal static class ProjectWriter
{
    public static void Write(Utf8JsonWriter w, Document doc)
    {
        w.WritePropertyName("project");
        w.WriteStartObject();

        Guarded(() =>
        {
            var info = doc.ProjectInformation;
            if (info is null) return;
            w.WritePropertyName("info");
            w.WriteStartObject();
            try { w.WriteString("name", info.Name); } catch { }
            try { w.WriteString("number", info.Number); } catch { }
            try { w.WriteString("address", info.Address); } catch { }
            try { w.WriteString("clientName", info.ClientName); } catch { }
            try { w.WriteString("status", info.Status); } catch { }
            try { w.WriteString("author", info.Author); } catch { }
            try { w.WriteString("buildingName", info.BuildingName); } catch { }
            try { w.WriteString("organizationName", info.OrganizationName); } catch { }
            w.WriteEndObject();
        });

        Guarded(() =>
        {
            var units = doc.GetUnits();
            w.WritePropertyName("units");
            w.WriteStartArray();
            foreach (var spec in UnitUtils.GetAllMeasurableSpecs())
            {
                w.WriteStartObject();
                try
                {
                    w.WriteString("spec", spec.TypeId);
                    var options = units.GetFormatOptions(spec);
                    w.WriteBoolean("useDefault", options.UseDefault);
                    try { w.WriteString("unit", options.GetUnitTypeId().TypeId); } catch { }
                    try { w.WriteNumber("accuracy", options.Accuracy); } catch { }
                }
                catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("levels");
            w.WriteStartArray();
            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                w.WriteStartObject();
                w.WriteNumber("id", level.Id.Value);
                try { w.WriteString("name", level.Name); } catch { }
                try { w.WriteNumber("elevation", level.Elevation); } catch { }
                try { w.WriteNumber("projectElevation", level.ProjectElevation); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("grids");
            w.WriteStartArray();
            foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                w.WriteStartObject();
                w.WriteNumber("id", grid.Id.Value);
                try { w.WriteString("name", grid.Name); } catch { }
                try
                {
                    if (grid.Curve is { } curve)
                    {
                        SnapshotWriter.WriteXyz(w, "start", curve.GetEndPoint(0));
                        SnapshotWriter.WriteXyz(w, "end", curve.GetEndPoint(1));
                    }
                }
                catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("phases");
            w.WriteStartArray();
            foreach (Phase phase in doc.Phases)
            {
                w.WriteStartObject();
                w.WriteNumber("id", phase.Id.Value);
                try { w.WriteString("name", phase.Name); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            if (!doc.IsWorkshared) return;
            w.WritePropertyName("worksets");
            w.WriteStartArray();
            foreach (var workset in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
            {
                w.WriteStartObject();
                w.WriteNumber("id", workset.Id.IntegerValue);
                try { w.WriteString("name", workset.Name); } catch { }
                try { w.WriteBoolean("isOpen", workset.IsOpen); } catch { }
                try { w.WriteBoolean("isEditable", workset.IsEditable); } catch { }
                try { w.WriteString("owner", workset.Owner); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("designOptions");
            w.WriteStartArray();
            foreach (var option in new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).Cast<DesignOption>())
            {
                w.WriteStartObject();
                w.WriteNumber("id", option.Id.Value);
                try { w.WriteString("name", option.Name); } catch { }
                try { w.WriteBoolean("isPrimary", option.IsPrimary); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("links");
            w.WriteStartArray();
            foreach (var linkType in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                w.WriteStartObject();
                w.WriteNumber("id", linkType.Id.Value);
                try { w.WriteString("name", linkType.Name); } catch { }
                try { w.WriteString("attachmentType", linkType.AttachmentType.ToString()); } catch { }
                try
                {
                    var reference = ExternalFileUtils.GetExternalFileReference(doc, linkType.Id);
                    if (reference is not null)
                        w.WriteString("path", ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath()));
                }
                catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WritePropertyName("linkInstances");
            w.WriteStartArray();
            foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                w.WriteStartObject();
                w.WriteNumber("id", instance.Id.Value);
                try { w.WriteNumber("typeId", instance.GetTypeId().Value); } catch { }
                try { w.WriteString("name", instance.Name); } catch { }
                try { SnapshotWriter.WriteXyz(w, "origin", instance.GetTotalTransform().Origin); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            var location = doc.ActiveProjectLocation;
            if (location is null) return;
            var position = location.GetProjectPosition(XYZ.Zero);
            if (position is null) return;
            w.WritePropertyName("position");
            w.WriteStartObject();
            try { w.WriteString("name", location.Name); } catch { }
            w.WriteNumber("eastWest", position.EastWest);
            w.WriteNumber("northSouth", position.NorthSouth);
            w.WriteNumber("elevation", position.Elevation);
            w.WriteNumber("angle", position.Angle);
            w.WriteEndObject();
        });

        Guarded(() =>
        {
            w.WritePropertyName("globalParams");
            w.WriteStartArray();
            foreach (var id in GlobalParametersManager.GetAllGlobalParameters(doc))
            {
                if (doc.GetElement(id) is not GlobalParameter gp) continue;
                w.WriteStartObject();
                w.WriteNumber("id", id.Value);
                try { w.WriteString("name", gp.Name); } catch { }
                try
                {
                    switch (gp.GetValue())
                    {
                        case DoubleParameterValue d:
                            w.WriteNumber("value", d.Value);
                            break;
                        case IntegerParameterValue i:
                            w.WriteNumber("value", i.Value);
                            break;
                        case StringParameterValue s:
                            w.WriteString("value", s.Value);
                            break;
                        case ElementIdParameterValue e:
                            w.WriteNumber("value", e.Value.Value);
                            break;
                    }
                }
                catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        Guarded(() =>
        {
            w.WritePropertyName("printSettings");
            w.WriteStartArray();
            foreach (var setting in new FilteredElementCollector(doc).OfClass(typeof(PrintSetting)))
            {
                w.WriteStartObject();
                w.WriteNumber("id", setting.Id.Value);
                try { w.WriteString("name", setting.Name); } catch { }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        // Document.PrintManager намеренно НЕ трогаем: обращение к нему заставляет Revit
        // проверить драйвер принтера и показать модальный диалог («Microsoft Print to PDF
        // cannot be used with … print settings»), а аддин не имеет права порождать диалоги.
        // Сами настройки печати уже выгружены выше как элементы PrintSetting.

        w.WriteEndObject();

        static void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }
    }
}
