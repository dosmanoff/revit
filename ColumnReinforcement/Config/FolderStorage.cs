using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnReinforcement.Config;

/// <summary>
/// Persists user picks in <see cref="ProjectInfo"/> via ExtensibleStorage so the
/// dialog can pre-populate them on the next open. Two independent schemas — one
/// for the JSON config folder (since PR-02), one for the CSV assignments path
/// (since PR-32 hotfix) — because the Revit ExtensibleStorage API does not
/// allow adding fields to an existing schema once it has been registered with
/// the document. Each schema therefore owns exactly one simple string field.
/// </summary>
public static class FolderStorage
{
    // v1 — JSON config folder. Shipped in PR-02; do not change the GUID or field name.
    private static readonly Guid FolderSchemaGuid = new("847D1965-6B76-4A6B-A82D-61D0E8948ADF");
    private const string FolderFieldName = "ConfigFolder";

    // Independent schema for the CSV assignments path. New GUID since the original
    // folder schema is already locked in the wild.
    private static readonly Guid CsvSchemaGuid = new("8B8E6705-F43C-4AF5-8955-586CF3463718");
    private const string CsvFieldName = "CsvPath";

    // ── JSON config folder ──────────────────────────────────────────────

    public static string? Get(Document doc) =>
        Read(doc, FolderSchemaGuid, FolderFieldName, CreateFolderSchema);

    public static void Set(Document doc, string folder) =>
        Write(doc, FolderSchemaGuid, FolderFieldName, folder ?? "", CreateFolderSchema);

    // ── CSV assignments path ────────────────────────────────────────────

    public static string? GetCsvPath(Document doc) =>
        Read(doc, CsvSchemaGuid, CsvFieldName, CreateCsvSchema);

    public static void SetCsvPath(Document doc, string csvPath) =>
        Write(doc, CsvSchemaGuid, CsvFieldName, csvPath ?? "", CreateCsvSchema);

    // ── Shared plumbing ─────────────────────────────────────────────────

    private static string? Read(Document doc, Guid schemaGuid, string field, Func<Schema> createSchema)
    {
        Schema schema = Schema.Lookup(schemaGuid) ?? createSchema();
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return null;

        try
        {
            string v = entity.Get<string>(field);
            return string.IsNullOrEmpty(v) ? null : v;
        }
        catch { return null; }
    }

    private static void Write(Document doc, Guid schemaGuid, string field, string value, Func<Schema> createSchema)
    {
        Schema schema = Schema.Lookup(schemaGuid) ?? createSchema();
        var entity = new Entity(schema);
        entity.Set(field, value);
        doc.ProjectInformation.SetEntity(entity);
    }

    private static Schema CreateFolderSchema()
    {
        var sb = new SchemaBuilder(FolderSchemaGuid);
        sb.SetSchemaName("ColumnReinforcementSettings");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(FolderFieldName, typeof(string));
        return sb.Finish();
    }

    private static Schema CreateCsvSchema()
    {
        var sb = new SchemaBuilder(CsvSchemaGuid);
        sb.SetSchemaName("ColumnReinforcementCsvPath");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(CsvFieldName, typeof(string));
        return sb.Finish();
    }
}
