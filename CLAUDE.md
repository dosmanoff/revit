# SmartViews Plugin — Revit 2025

## Project goal
Batch view creation plugin for Revit 2025 (.NET 8, C#).
Reference product: https://docs.besmart.software/common-for-several-products/smart-views

## Key decisions already made
- Config system: JSON files, stored in user-defined folder, persisted via ExtensibleStorage
- View creation: ViewSection.CreateSection / ViewPlan.Create / View3D.CreateIsometric
- Naming: token-based template {Mark}, {Level}, {Type}, {Index}
- Crop box: computed from element BoundingBoxXYZ + configurable offset
- UI: WPF dialogs, single TransactionGroup per run

## Phase 1 scope (current)
- Ribbon button via IExternalApplication
- Config model + JSON loader
- Section/elevation from bounding box
- Token naming + duplicate handling
- Basic WPF dialog + error summary

## References
- Dev plan: smart-views-dev-plan.md (in this folder)
- Revit API docs: https://help.autodesk.com/view/RVT/2025/ENU/
- API class browser: https://www.revitapidocs.com/2025/
- Be.Smart Smart Views: https://docs.besmart.software/common-for-several-products/smart-views
