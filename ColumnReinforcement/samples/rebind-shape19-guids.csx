// One-shot tool: rebind 28_Z_Frame's A/B/C/D/J shared parameters
// to use the SAME shared-parameter GUIDs as the standard ACI shape '19'
// in the project. Output is saved as 28_Z_Frame_v19.rfa next to this
// script.
//
// Use via revit-mcp `send_code_to_revit` (transactionMode: none — this
// script manages its own transactions inside the family doc).
//
// Background: see ../docs/shape-pin-guid-finding.md. Quick version:
// `28_Z_Frame` is the only V-D-V cranked splice shape in the user's
// 21STR project, BUT its A/B/C/D/J shared params have different GUIDs
// than shape 19, and all 60+ project schedules' A/B/C/D fields are
// bound to shape 19's GUIDs. So bars in 28_Z_Frame fail to populate
// schedules even though the field names match.
//
// The fix: rebind 28_Z_Frame's shared params to shape 19's GUIDs.
// FamilyManager.ReplaceParameter(currentFp, externalDef, ...) refuses
// to replace a shared param directly with another shared param if the
// shape definition has segment-length constraints bound to the
// current FamilyParameter — Revit just returns 'Parameter replacement
// failed.' with no detail.
//
// Workaround: two-step swap via a non-shared family-param intermediate.
// Each ReplaceParameter call preserves the FamilyParameter Id (and
// therefore the segment-constraint binding), so the constraint
// "follows" the parameter through both swaps.
//
//   shared GUID=old → family-param (non-shared, temp name)
//   family-param   → shared GUID=new

var log = new System.Text.StringBuilder();
var app = document.Application;
string origSpFile = app.SharedParametersFilename;

const long SHAPE_28_FAMILY_ID = 83406L;   // Family of '28_Z_Frame' in the project. Adjust if different.
const string SP_FILE_PATH = @"C:\Users\Vic\Documents\Claude\Projects\Revit\shape19-params.txt";
const string OUTPUT_RFA_PATH = @"C:\Users\Vic\Documents\Claude\Projects\Revit\28_Z_Frame_v19.rfa";
const string SEED_RFA_PATH = @"C:\Users\Vic\Documents\Claude\Projects\Revit\28_Z_Frame_seed.rfa";

try {
    app.SharedParametersFilename = SP_FILE_PATH;
    var defFile = app.OpenSharedParameterFile();
    var spGroup = defFile.Groups.get_Item("Shape19");
    var defs = new System.Collections.Generic.Dictionary<string, ExternalDefinition>();
    foreach (var name in new[] {"A", "B", "C", "D", "J"})
        defs[name] = spGroup.Definitions.get_Item(name) as ExternalDefinition;

    // Step 0: save current family from project to disk (untouched seed)
    var fam = document.GetElement(new ElementId(SHAPE_28_FAMILY_ID)) as Family;
    var seedDoc = document.EditFamily(fam);
    seedDoc.SaveAs(SEED_RFA_PATH, new SaveAsOptions { OverwriteExistingFile = true });
    seedDoc.Close(false);

    // Step 1: open seed as standalone family doc (independent of project context)
    var famDoc = app.OpenDocumentFile(SEED_RFA_PATH);
    var fmgr = famDoc.FamilyManager;

    // Step 2: for each of A/B/C/D/J — swap shared GUID via the two-step trick.
    foreach (var name in new[] {"A", "B", "C", "D", "J"}) {
        FamilyParameter fp = null;
        foreach (FamilyParameter q in fmgr.Parameters)
            if (q.Definition?.Name == name) { fp = q; break; }
        if (fp == null) { log.AppendLine("[" + name + "] not found — skip"); continue; }

        var grp = fp.Definition.GetGroupTypeId();
        bool inst = fp.IsInstance;
        string tempName = name + "_tmp";

        // 2a: shared → family-param (temp name)
        using (var tx = new Transaction(famDoc, "Shared→Family " + name)) {
            tx.Start();
            fmgr.ReplaceParameter(fp, tempName, grp, inst);
            tx.Commit();
        }

        FamilyParameter fpTmp = null;
        foreach (FamilyParameter q in fmgr.Parameters)
            if (q.Definition?.Name == tempName) { fpTmp = q; break; }

        // 2b: family-param → shared (with new GUID; name resets to ExternalDefinition's "A"/"B"/...)
        using (var tx = new Transaction(famDoc, "Family→Shared " + name)) {
            tx.Start();
            fmgr.ReplaceParameter(fpTmp, defs[name], grp, inst);
            tx.Commit();
        }
        log.AppendLine("[" + name + "] rebound to GUID " + defs[name].GUID);
    }

    famDoc.SaveAs(OUTPUT_RFA_PATH, new SaveAsOptions { OverwriteExistingFile = true });
    famDoc.Close(false);
    app.SharedParametersFilename = origSpFile;

    // Hand back to LoadFamily(OUTPUT_RFA_PATH, out family) to load into project.
    return "Saved " + OUTPUT_RFA_PATH + "; rebound 5 shared params. Now LoadFamily it.";
} catch (Exception ex) {
    app.SharedParametersFilename = origSpFile;
    return "FAILED: " + ex.Message + "\n" + ex.StackTrace;
}
