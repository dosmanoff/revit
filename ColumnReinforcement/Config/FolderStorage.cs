using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnReinforcement.Config;

/// <summary>
/// Persists the user's chosen config folder in <see cref="ProjectInfo"/> via ExtensibleStorage,
/// so it travels with the .rvt instead of living in a user-machine setting.
///
/// Distinct schema GUID from <c>WallReinforcement.Config.FolderStorage</c> — each plugin
/// keeps its own folder preference independent of the others.
/// </summary>
public static class FolderStorage
{
    private static readonly Guid SchemaGuid = new("847D1965-6B76-4A6B-A82D-61D0E8948ADF");
    private const string FieldFolder  = "ConfigFolder";
    private const string FieldCsvPath = "CsvPath";

    // ── Config folder (single-config mode, unchanged from earlier PRs) ──

    public static string? Get(Document doc) => ReadField(doc, FieldFolder);

    public static void Set(Document doc, string folder) => WriteField(doc, FieldFolder, folder ?? "");

    // ── CSV path (per-column "From CSV" mode, new in PR-32) ─────────────

    public static string? GetCsvPath(Document doc) => ReadField(doc, FieldCsvPath);

    public static void SetCsvPath(Document doc, string csvPath) => WriteField(doc, FieldCsvPath, csvPath ?? "");

    // ── Schema + field plumbing ─────────────────────────────────────────

    private static string? ReadField(Document doc, string field)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return null;

        // Entities created under the v1 schema (no CsvPath) throw when reading the
        // newer field — treat as "not set" rather than blowing up.
        try
        {
            string v = entity.Get<string>(field);
            return string.IsNullOrEmpty(v) ? null : v;
        }
        catch { return null; }
    }

    private static void WriteField(Document doc, string field, string value)
    {
        Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();
        Entity entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid())
            entity = new Entity(schema);

        // Both fields must be set on the entity. Preserve any value already present
        // for the OTHER field so writing CsvPath doesn't wipe ConfigFolder.
        string folder  = field == FieldFolder  ? value : SafeGet(entity, FieldFolder);
        string csvPath = field == FieldCsvPath ? value : SafeGet(entity, FieldCsvPath);

        var fresh = new Entity(schema);
        fresh.Set(FieldFolder,  folder  ?? "");
        fresh.Set(FieldCsvPath, csvPath ?? "");
        doc.ProjectInformation.SetEntity(fresh);
    }

    private static string SafeGet(Entity entity, string field)
    {
        try { return entity.Get<string>(field) ?? ""; }
        catch { return ""; }
    }

    private static Schema CreateSchema()
    {
        var sb = new SchemaBuilder(SchemaGuid);
        sb.SetSchemaName("ColumnReinforcementSettings");
        sb.SetReadAccessLevel(AccessLevel.Public);
        sb.SetWriteAccessLevel(AccessLevel.Public);
        sb.AddSimpleField(FieldFolder,  typeof(string));
        sb.AddSimpleField(FieldCsvPath, typeof(string));
        return sb.Finish();
    }
}
