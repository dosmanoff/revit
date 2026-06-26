# WallReinforcement → Slab parity — next-session handoff

Bringing `WallReinforcement` to `SlabReinforcement` parity (functionality + reliability).
**Reliability + WallViews + hooked ties are DONE and committed.** This doc hands off the
remaining functional work (#5 Dump+brief, #6 ACI, #7 finalize).

> Start by reading memories `proj-wall-reinforcement-parity` and
> `revit-wall-areareinforcement-gotchas`, plus `../revit-mcp-api-cookbook.md`.

---

## Prompt (paste as the first message)

> Продолжаем **WallReinforcement** → паритет со SlabReinforcement, ветка
> `claude/wall-reinforcement-slab-parity`. Прочитай этот файл целиком
> (`WallReinforcement/HANDOFF-next-session.md`) и memory `proj-wall-reinforcement-parity` +
> `revit-wall-areareinforcement-gotchas` — там параметры, файлы-эталоны Slab, ALC-рецепт живого
> теста, сниппет подавления диалогов со сбором ошибок и список гоч.
>
> Готово и закоммичено (4 коммита): надёжность (rebar-сеты + failures-preprocessor +
> `Geometry`/`Tests`, 21 тест), WallViews (движок + кнопка «Wall Views»), крюковые шпильки 135° —
> всё провалидировано вживую на 6 стенах `WRTEST`.
>
> Сделай оставшееся, валидируя каждый блок вживую через ALC и коммитя по ходу (в конце — PR +
> self-merge по `merge-authorization`):
> - **#5 WallDump + JSON-brief** — экспорт геометрии стены в JSON (грани/проёмы/кромки/стыки) +
>   структурированный brief с loader/mapper; зеркалить SlabDump/SlabDumpBuilder/SlabBrief/
>   BriefLoader/BriefMapper; добавить `wall-dump-schema.md` + `wall-brief-schema.md`.
> - **#6 ACI-калькулятор** анкеровки/нахлёста (ACI 318-19) — зеркалить AciAnchorageCalculator;
>   подставить в extension проёмов, legLength кромок, lapLength углов/Т + панель/команда в UI.
> - **#7 Финал** — диалог + ExtensibleStorage для `WallViewsConfig` (сейчас `Default()`); фикс
>   `tx.Commit()==RolledBack` в `WallReinforcer.Run` (упавшая по regen стена помечается Success);
>   README/CLAUDE.md/wall-reinforcement-dev-plan.md; deploy обеих DLL; уборка тестовых видов; PR.
>
> Тестовая модель `Empty_project_ArtDreamTJ4X9_detached` открыта; стены `WRTEST`
> id 6318852–6318857; продакшн «7-15 Terrace View Avenue» НЕ трогать; запись —
> `transactionMode:"none"` + своя транзакция.

---

## Parameters

**Repo / branch**
- Root `L:\My Drive\claude\revit` · plugin `WallReinforcement\` · branch
  `claude/wall-reinforcement-slab-parity` (off Stairs branch `claude/strange-villani-b8c0f9`)
- Commits: `f204753` reliability · `d721534` WallViews · `3be8f2a` tie hooks
- Build: `dotnet build WallReinforcement/WallReinforcement.csproj -c Release` (pulls in
  `.Geometry`). Tests: `dotnet test WallReinforcement.Tests/WallReinforcement.Tests.csproj`

**Slab references to mirror** — worktree
`L:\My Drive\claude\revit\SlabReinforcement\.claude\worktrees\pensive-gould-e4b82c\SlabReinforcement\`
- `Export\SlabDump.cs`, `Export\SlabDumpBuilder.cs`
- `Config\SlabBrief.cs`, `Config\BriefLoader.cs`, `Config\BriefMapper.cs`
- ACI: `AciAnchorageCalculator` (in `SlabReinforcement.Geometry`)
- Config persistence: `Config\SlabViewsConfigStore.cs`
- Schema docs: `slab-dump-schema.md`, `slab-brief-schema.md`, `agent-config-guide.md`

**Live test via ALC harness** (memory `revit-test-fresh-dll-via-alc`)
- Copy BOTH `WallReinforcement.dll` and `WallReinforcement.Geometry.dll` to a fresh
  `C:\dev\wrtestN\`; load with a collectible `AssemblyLoadContext` (resolver: local dir,
  else `AssemblyLoadContext.Default`)
- Reinforce: `ConfigLoader.Load(cfgPath)` → `new WallReinforcer(doc)` →
  `ReinforceOne(wall, cfg)` inside your own `Transaction` under `transactionMode:"none"`
  (persists), or `Run([ids], cfg, false)`
- Views: `new WallViewsEngine(doc, WallViewsConfig.Default()).Run([ids])`
- Config sample: `WallReinforcement\samples\aci-full-12in.json` (model is ACI: bar types
  `#3..#20`, 1 `AreaReinforcementType`, hook `Stirrup/Tie - 135 deg.`)

**Dialog suppression + error capture** (use for every live write)
```csharp
var caught = new Dictionary<string,int>();
app.FailuresProcessing += (s,e) => {
  var fa = e.GetFailuresAccessor();
  foreach (var fm in fa.GetFailureMessages())                       // capture text
     caught[fm.GetSeverity()+": "+fm.GetDescriptionText()] =
        caught.TryGetValue(..., out var n) ? n+1 : 1;
  fa.DeleteAllWarnings();
  e.SetProcessingResult(fa.GetFailureMessages().Count > 0
     ? FailureProcessingResult.ProceedWithRollBack                  // suppress modal
     : FailureProcessingResult.Continue);
};
```
Visual verify: `ExportImage` (PixelSize ≤ 1900, `ExportRange.SetOfViews`) → read the PNG.

**Gotchas** (memory `revit-wall-areareinforcement-gotchas`)
- `MaximumSpacing` on a WALL `AreaReinforcement` → "circular chain of references" regen-fail —
  use the default `NumberWithSpacing` (auto-fills from spacing).
- Place bars off `Level.ProjectElevation`, NOT `Level.Elevation` (base-point offset).
- `AreaReinforcement` child bars (`RebarInSystem`) are untagged `OST_Rebar` — keep them by
  ownership via `GetRebarInSystemIds()`, else isolate-by-tag hides the mesh.
- A hook type MUST match the bar style (`StirrupTie` bar + `Stirrup/Tie` hook;
  `Standard` bar + `Standard` hook). Mismatch → `InternalException`.
- Use `transactionMode:"none"` + your own transaction for live writes (auto-mode fights the
  rollback). Two consecutive bridge timeouts usually = an open Revit modal, not a dead bridge.

**Test walls** (in `Empty_project_ArtDreamTJ4X9_detached`)
| id | type | role |
|----|------|------|
| 6318852 | TH 8" | straight |
| 6318853 | TH 12" | straight (add an opening to test trim) |
| 6318854 / 6318855 | TH 10" | L-corner pair |
| 6318856 | TH 14" | through wall of the T |
| 6318857 | TH 8" | T-junction stem |

Cleanup pending: many test views/schedules/sheets accumulated from WallViews runs; the
`WRTEST` walls + `WR:`-tagged rebar can be deleted when done.
