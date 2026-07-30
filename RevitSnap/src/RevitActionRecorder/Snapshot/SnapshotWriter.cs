using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Autodesk.Revit.DB;
using RevitActionRecorder.Configuration;
using RevitActionRecorder.Recording;

namespace RevitActionRecorder.Snapshot;

internal sealed class SnapshotResult
{
    public bool Cancelled { get; set; }
    public string Path { get; set; } = "";
    public long Instances { get; set; }
    public long Types { get; set; }
    public long Views { get; set; }
    public TimeSpan Took { get; set; }
}

/// <summary>
/// Полный снапшот модели: потоковая запись через Utf8JsonWriter прямо в GZipStream → FileStream.
/// Объектное дерево модели в памяти не строится. Документ не модифицируется.
/// </summary>
internal static class SnapshotWriter
{
    public static SnapshotResult Take(
        Document doc,
        string filePath,
        RecorderConfig config,
        ParamCache? cache,
        SessionLog log,
        IntPtr ownerWindow)
    {
        var started = DateTime.UtcNow;
        var result = new SnapshotResult { Path = filePath };
        var progress = new ProgressWindow("RevitActionRecorder — снапшот", ownerWindow);
        progress.Show();

        FileStream? fileStream = null;
        Stream? stream = null;
        Utf8JsonWriter? w = null;
        try
        {
            fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream = config.Gzip ? new GZipStream(fileStream, CompressionLevel.Fastest) : fileStream;
            w = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            w.WriteStartObject();
            w.WriteNumber("schema", Model.Schema.Version);
            w.WriteString("kind", "snapshot");
            w.WriteString("takenAt", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture));
            w.WriteStartObject("model");
            w.WriteString("title", doc.Title);
            w.WriteString("path", doc.PathName);
            try { w.WriteBoolean("workshared", doc.IsWorkshared); } catch { }
            w.WriteEndObject();

            Section("project", () => ProjectWriter.Write(w, doc));
            Section("categories", () => DressingWriter.WriteObjectStyles(w, doc));
            Section("resources", () => DressingWriter.WriteResources(w, doc));

            var typeCollector = new FilteredElementCollector(doc).WhereElementIsElementType();
            var instanceCollector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            long typeCount = typeCollector.GetElementCount();
            long instanceCount = instanceCollector.GetElementCount();
            long done = 0;
            long total = Math.Max(1, typeCount + instanceCount);

            w.WritePropertyName("types");
            w.WriteStartArray();
            foreach (var element in new FilteredElementCollector(doc).WhereElementIsElementType())
            {
                WriteElement(w, doc, element, config.GeometryHash, cache);
                Tick(ref done, total, progress, "Типы и экземпляры");
            }
            w.WriteEndArray();

            w.WritePropertyName("instances");
            w.WriteStartArray();
            foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                WriteElement(w, doc, element, config.GeometryHash, cache);
                Tick(ref done, total, progress, "Типы и экземпляры");
            }
            w.WriteEndArray();

            result.Types = typeCount;
            result.Instances = instanceCount;

            progress.Report("Оформление: виды, переопределения, листы, спецификации…", null);
            result.Views = DressingWriter.WriteViews(w, doc, config, progress);
            Section("sheets", () => DressingWriter.WriteSheets(w, doc));
            Section("schedules", () => DressingWriter.WriteSchedules(w, doc));
            Section("legends", () => DressingWriter.WriteLegends(w, doc));
            Section("warnings", () => DressingWriter.WriteWarnings(w, doc));

            w.WriteEndObject();
            w.Flush();
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        catch (Exception ex)
        {
            log.Error("SnapshotWriter", ex);
            result.Cancelled = true;
        }
        finally
        {
            try { w?.Dispose(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { fileStream?.Dispose(); } catch { }
            progress.Close();
        }

        if (result.Cancelled)
        {
            try { File.Delete(filePath); } catch { }
        }

        result.Took = DateTime.UtcNow - started;
        log.Info($"snapshot {(result.Cancelled ? "CANCELLED" : "ok")}: {System.IO.Path.GetFileName(filePath)}, " +
                 $"types={result.Types}, instances={result.Instances}, views={result.Views}, took={result.Took.TotalSeconds:0.0}s");
        return result;

        void Section(string name, Action write)
        {
            progress.Report($"Раздел: {name}…", null);
            if (progress.CancelRequested) throw new OperationCanceledException();
            try
            {
                write();
            }
            catch (Exception ex)
            {
                log.Error($"Snapshot.{name}", ex);
            }
        }
    }

    private static void Tick(ref long done, long total, ProgressWindow progress, string stage)
    {
        done++;
        if (done % 250 != 0)
            return;
        progress.Report($"{stage}: {done} / {total}", (double)done / total);
        if (progress.CancelRequested)
            throw new OperationCanceledException();
    }

    internal static void WriteElement(Utf8JsonWriter w, Document doc, Element element, bool geometryHash, ParamCache? cache)
    {
        w.WriteStartObject();
        try
        {
            w.WriteNumber("id", element.Id.Value);
            w.WriteString("uid", element.UniqueId);
            try { w.WriteString("name", element.Name); } catch { }

            var category = element.Category;
            if (category is not null)
            {
                w.WriteString("cat", category.Name);
                w.WriteNumber("catId", category.Id.Value);
            }
            w.WriteString("cls", element.GetType().Name);

            try
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    w.WriteNumber("typeId", typeId.Value);
                    if (doc.GetElement(typeId) is ElementType et)
                    {
                        w.WriteString("family", et.FamilyName);
                        w.WriteString("type", et.Name);
                    }
                }
                else if (element is ElementType ownType)
                {
                    w.WriteString("family", ownType.FamilyName);
                }
            }
            catch { }

            try
            {
                if (element.LevelId != ElementId.InvalidElementId && doc.GetElement(element.LevelId) is Level level)
                    w.WriteString("level", level.Name);
            }
            catch { }

            try
            {
                if (doc.IsWorkshared && element.WorksetId != WorksetId.InvalidWorksetId)
                {
                    var workset = doc.GetWorksetTable().GetWorkset(element.WorksetId);
                    if (workset is not null) w.WriteString("workset", workset.Name);
                }
            }
            catch { }

            try
            {
                var option = element.DesignOption;
                if (option is not null) w.WriteString("designOption", option.Name);
            }
            catch { }

            try
            {
                if (element.CreatedPhaseId != ElementId.InvalidElementId && doc.GetElement(element.CreatedPhaseId) is Phase created)
                    w.WriteString("phaseCreated", created.Name);
                if (element.DemolishedPhaseId != ElementId.InvalidElementId && doc.GetElement(element.DemolishedPhaseId) is Phase demolished)
                    w.WriteString("phaseDemolished", demolished.Name);
            }
            catch { }

            try
            {
                if (element.GroupId != ElementId.InvalidElementId)
                    w.WriteNumber("group", element.GroupId.Value);
            }
            catch { }

            try
            {
                if (element.AssemblyInstanceId != ElementId.InvalidElementId)
                    w.WriteNumber("assembly", element.AssemblyInstanceId.Value);
            }
            catch { }

            var records = ParameterExtractor.Extract(element);
            cache?.Put(element.Id.Value, records);
            w.WritePropertyName("params");
            w.WriteStartArray();
            foreach (var r in records)
            {
                w.WriteStartObject();
                w.WriteNumber("pid", r.Pid);
                w.WriteString("name", r.Name);
                if (r.Bip is not null) w.WriteString("bip", r.Bip);
                if (r.Guid is not null) w.WriteString("guid", r.Guid);
                w.WriteString("storage", r.Storage);
                if (r.DataType is not null) w.WriteString("dataType", r.DataType);
                if (r.Raw is not null) w.WriteString("raw", r.Raw);
                if (r.Display is not null) w.WriteString("display", r.Display);
                if (r.ReadOnly) w.WriteBoolean("ro", true);
                if (r.Shared) w.WriteBoolean("shared", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            try
            {
                var box = element.get_BoundingBox(null);
                if (box is not null)
                {
                    w.WritePropertyName("bbox");
                    w.WriteStartObject();
                    WriteXyz(w, "min", box.Min);
                    WriteXyz(w, "max", box.Max);
                    w.WriteEndObject();
                }
            }
            catch { }

            try
            {
                switch (element.Location)
                {
                    case LocationPoint lp:
                        w.WritePropertyName("loc");
                        w.WriteStartObject();
                        WriteXyz(w, "point", lp.Point);
                        try { w.WriteNumber("rotation", lp.Rotation); } catch { }
                        w.WriteEndObject();
                        break;
                    case LocationCurve lc when lc.Curve is not null:
                        w.WritePropertyName("loc");
                        w.WriteStartObject();
                        w.WriteString("curve", lc.Curve.GetType().Name);
                        try
                        {
                            WriteXyz(w, "start", lc.Curve.GetEndPoint(0));
                            WriteXyz(w, "end", lc.Curve.GetEndPoint(1));
                        }
                        catch { }
                        if (lc.Curve is Arc arc)
                        {
                            try
                            {
                                WriteXyz(w, "center", arc.Center);
                                w.WriteNumber("radius", arc.Radius);
                            }
                            catch { }
                        }
                        w.WriteEndObject();
                        break;
                }
            }
            catch { }

            if (geometryHash)
            {
                var hash = GeometryHasher.Hash(element);
                if (hash is not null) w.WriteString("geomHash", hash);
            }
        }
        catch
        {
        }
        w.WriteEndObject();
    }

    internal static void WriteXyz(Utf8JsonWriter w, string name, XYZ point)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        w.WriteNumberValue(Math.Round(point.X, 9));
        w.WriteNumberValue(Math.Round(point.Y, 9));
        w.WriteNumberValue(Math.Round(point.Z, 9));
        w.WriteEndArray();
    }
}
