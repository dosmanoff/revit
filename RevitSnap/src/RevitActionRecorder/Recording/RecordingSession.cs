using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitActionRecorder.Configuration;
using RevitActionRecorder.Imaging;
using RevitActionRecorder.Model;
using RevitActionRecorder.Snapshot;

namespace RevitActionRecorder.Recording;

internal sealed record SessionStats(
    string Folder,
    long TotalEvents,
    IReadOnlyDictionary<string, long> Counts,
    int UndoneMarked,
    bool AfterSnapshotTaken,
    bool DiffStarted);

/// <summary>
/// Одна сессия записи, привязанная к конкретному документу. Все подписки живут здесь.
/// Каждый обработчик целиком в try/catch: ошибки в log.txt, наружу не выходят.
/// Документ никогда не модифицируется.
/// </summary>
internal sealed class RecordingSession
{
    private readonly UIApplication _uiapp;
    private readonly Autodesk.Revit.ApplicationServices.Application _app;
    private readonly Document _doc;
    private readonly string _docTitle;
    private readonly RecorderConfig _config;
    private readonly JsonlWriter _writer;
    private readonly SessionLog _log;
    private readonly CommandTracker _commands = new();
    private readonly UndoTracker _undo = new();
    private readonly Dictionary<string, long> _counts = new();
    private readonly string _eventsPath;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly ParamDefRegistry _paramDefs = new();
    private readonly ParamCache _paramCache;
    private long _seq;
    private int _midCounter;
    private bool _ribbonHooked;
    private EventHandler<Autodesk.Internal.Windows.RibbonItemExecutedEventArgs>? _ribbonHandler;

    public string Folder { get; }

    public bool IsActive { get; private set; }

    /// <summary>Фоновый расчёт diff.json после Stop; OnShutdown дожидается его.</summary>
    public Task? DiffTask { get; private set; }

    /// <summary>null — пользователь отменил снапшот «до», запись не началась.</summary>
    public static RecordingSession? Start(UIApplication uiapp, Document doc, RecorderConfig config)
    {
        var session = new RecordingSession(uiapp, doc, config);

        var before = SnapshotWriter.Take(
            doc, session.SnapshotPath("snapshot_before"), config, session._paramCache, session._log,
            uiapp.MainWindowHandle, session._paramDefs);
        if (before.Cancelled)
        {
            session._log.Info("before-snapshot cancelled by user - session aborted");
            session._writer.Complete(TimeSpan.FromSeconds(2));
            session.IsActive = false;
            return null;
        }
        session._paramCache.Ready = true;

        session.ExportShots("before", "view");
        session.Subscribe();
        session.EmitSessionStart();
        session._log.Info($"recording started: '{session._docTitle}' -> {session.Folder}");
        return session;
    }

    private RecordingSession(UIApplication uiapp, Document doc, RecorderConfig config)
    {
        _uiapp = uiapp;
        _app = uiapp.Application;
        _doc = doc;
        _docTitle = doc.Title;
        _config = config;

        Folder = Path.Combine(
            config.OutputRoot,
            Sanitize(doc.Title),
            $"session_{DateTime.Now:yyyy-MM-dd_HHmmss}");
        Directory.CreateDirectory(Folder);

        _log = new SessionLog(Path.Combine(Folder, "log.txt"));
        _paramCache = new ParamCache(_paramDefs, config.SkipWorksharingParams);
        _eventsPath = Path.Combine(Folder, "events.jsonl");
        WriteManifest(finishedAt: null, undoneMarked: null);
        _writer = new JsonlWriter(_eventsPath, _log);
        IsActive = true;
    }

    // ---- жизненный цикл ---------------------------------------------------

    public SessionStats Stop(bool takeAfterSnapshot = true)
    {
        var counts = new Dictionary<string, long>(_counts);
        if (!IsActive)
            return new SessionStats(Folder, _seq, counts, 0, false, false);

        IsActive = false;
        Unsubscribe();

        bool afterTaken = false;
        var afterPath = SnapshotPath("snapshot_after");
        if (takeAfterSnapshot)
        {
            try
            {
                var after = SnapshotWriter.Take(
                    _doc, afterPath, _config, cache: null, _log, _uiapp.MainWindowHandle, _paramDefs);
                afterTaken = !after.Cancelled;
            }
            catch (Exception ex)
            {
                _log.Error("AfterSnapshot", ex);
            }
            ExportShots("after", "view");
        }

        Emit(new EventRecord { Kind = "session_stop", Counts = new Dictionary<string, long>(_counts) });
        _writer.Complete(TimeSpan.FromSeconds(10));

        int undoneMarked = 0;
        try
        {
            undoneMarked = EventsFinalizer.MarkUndone(_eventsPath, _undo.UndoneSeqs, _log);
        }
        catch (Exception ex)
        {
            _log.Error("EventsFinalizer", ex);
        }

        bool diffStarted = false;
        var beforePath = SnapshotPath("snapshot_before");
        if (afterTaken && File.Exists(beforePath) && File.Exists(afterPath))
        {
            var diffPath = Path.Combine(Folder, "diff.json");
            var log = _log;
            DiffTask = Task.Run(() =>
            {
                try
                {
                    Diff.DiffEngine.DiffFiles(beforePath, afterPath, diffPath);
                    log.Info("diff.json written");
                }
                catch (Exception ex)
                {
                    log.Error("DiffEngine", ex);
                }
            });
            diffStarted = true;
        }

        counts = new Dictionary<string, long>(_counts);
        WriteManifest(finishedAt: DateTimeOffset.Now, undoneMarked: undoneMarked);
        _log.Info($"recording stopped: {_seq} events, {undoneMarked} marked undone, after-snapshot={afterTaken}");
        return new SessionStats(Folder, _seq, counts, undoneMarked, afterTaken, diffStarted);
    }

    /// <summary>Промежуточный снапшот без остановки записи; null при отмене.</summary>
    public string? TakeMidSnapshot()
    {
        var path = SnapshotPath($"snapshot_mid_{++_midCounter:00}");
        var result = SnapshotWriter.Take(
            _doc, path, _config, cache: null, _log, _uiapp.MainWindowHandle, _paramDefs);
        if (result.Cancelled)
        {
            _midCounter--;
            return null;
        }
        Emit(new EventRecord { Kind = "snapshot", Note = Path.GetFileName(path) });
        return path;
    }

    /// <summary>Mark moment: комментарий отдельным событием + PNG активного вида.</summary>
    public void Mark(string comment)
    {
        var seq = Emit(new EventRecord { Kind = "mark", Note = comment, View = TryActiveView() });
        try
        {
            var active = _uiapp.ActiveUIDocument?.ActiveView;
            if (active is not null && IsOurDoc(active.Document) && active.CanBePrinted)
            {
                ImageExportQueue.Instance.Enqueue(new ImageExportQueue.Request(
                    _doc, [active.Id], Path.Combine(Folder, "shots", "marks"),
                    $"mark_{seq:000}", _config.ImagePixelSize));
            }
        }
        catch (Exception ex)
        {
            _log.Error("Mark", ex);
        }
    }

    private string SnapshotPath(string baseName) =>
        Path.Combine(Folder, baseName + ".json" + (_config.Gzip ? ".gz" : ""));

    private void ExportShots(string subfolder, string prefix)
    {
        try
        {
            var views = ResolveShotViews();
            if (views.Count == 0)
            {
                _log.Warn($"shots/{subfolder}: no views matched the config, nothing to export");
                return;
            }
            ImageExportQueue.Instance.Enqueue(new ImageExportQueue.Request(
                _doc, views, Path.Combine(Folder, "shots", subfolder), prefix, _config.ImagePixelSize));
        }
        catch (Exception ex)
        {
            _log.Error("ExportShots", ex);
        }
    }

    private List<ElementId> ResolveShotViews()
    {
        var result = new List<ElementId>();
        try
        {
            if (_config.SnapshotViews.Count == 0)
            {
                var active = _uiapp.ActiveUIDocument?.ActiveView;
                if (active is not null && IsOurDoc(active.Document) && active.CanBePrinted)
                    result.Add(active.Id);
                return result;
            }

            foreach (var view in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
            {
                if (view.IsTemplate || !view.CanBePrinted)
                    continue;
                string name;
                try { name = view.Name; }
                catch { continue; }

                foreach (var pattern in _config.SnapshotViews)
                {
                    if (name == pattern || SafeRegexMatch(name, pattern))
                    {
                        result.Add(view.Id);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("ResolveShotViews", ex);
        }
        return result;
    }

    private static bool SafeRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
        }
        catch
        {
            return false;
        }
    }

    private void Subscribe()
    {
        _app.DocumentChanged += OnDocumentChanged;
        _app.FailuresProcessing += OnFailuresProcessing;
        _app.DocumentOpened += OnDocumentOpened;
        _app.DocumentClosing += OnDocumentClosing;
        _app.DocumentSaved += OnDocumentSaved;
        _app.DocumentSavedAs += OnDocumentSavedAs;
        _app.DocumentSynchronizedWithCentral += OnDocumentSynchronized;
        _app.DocumentPrinted += OnDocumentPrinted;

        _uiapp.DialogBoxShowing += OnDialogBoxShowing;
        _uiapp.ViewActivated += OnViewActivated;
        _uiapp.SelectionChanged += OnSelectionChanged;

        // Подписка на ленту недокументирована — сбой не мешает остальной записи.
        try
        {
            _ribbonHandler = OnRibbonItemExecuted;
            Autodesk.Windows.ComponentManager.ItemExecuted += _ribbonHandler;
            _ribbonHooked = true;
        }
        catch (Exception ex)
        {
            _log.Warn($"ribbon hook unavailable, commands won't be correlated: {ex.Message}");
        }
    }

    private void Unsubscribe()
    {
        try
        {
            _app.DocumentChanged -= OnDocumentChanged;
            _app.FailuresProcessing -= OnFailuresProcessing;
            _app.DocumentOpened -= OnDocumentOpened;
            _app.DocumentClosing -= OnDocumentClosing;
            _app.DocumentSaved -= OnDocumentSaved;
            _app.DocumentSavedAs -= OnDocumentSavedAs;
            _app.DocumentSynchronizedWithCentral -= OnDocumentSynchronized;
            _app.DocumentPrinted -= OnDocumentPrinted;

            _uiapp.DialogBoxShowing -= OnDialogBoxShowing;
            _uiapp.ViewActivated -= OnViewActivated;
            _uiapp.SelectionChanged -= OnSelectionChanged;
        }
        catch (Exception ex)
        {
            _log.Error("Unsubscribe", ex);
        }

        if (_ribbonHooked && _ribbonHandler is not null)
        {
            try { Autodesk.Windows.ComponentManager.ItemExecuted -= _ribbonHandler; }
            catch (Exception ex) { _log.Error("Unsubscribe.Ribbon", ex); }
        }
    }

    // ---- обработчики ------------------------------------------------------

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        try
        {
            var doc = e.GetDocument();
            if (!IsOurDoc(doc)) return;

            var op = e.Operation;
            List<string> txn = e.GetTransactionNames()?.ToList() ?? [];
            var added = new List<ElementBrief>();
            foreach (var id in e.GetAddedElementIds())
            {
                var brief = ElementExtractor.Brief(doc, id);
                if (brief is null) continue;
                added.Add(brief);
                if (_paramCache.Ready)
                {
                    var element = doc.GetElement(id);
                    if (element is not null)
                        _paramCache.Put(
                            id.Value,
                            ParameterExtractor.Extract(element, _paramDefs),
                            LocationSignature.Of(element));
                }
            }

            var modified = new List<ElementBrief>();
            foreach (var id in e.GetModifiedElementIds())
            {
                var brief = ElementExtractor.Brief(doc, id);
                if (brief is null) continue;
                if (_paramCache.Ready)
                {
                    var element = doc.GetElement(id);
                    if (element is not null)
                    {
                        brief.Params = _paramCache.DiffParams(
                            id.Value, ParameterExtractor.Extract(element, _paramDefs));
                        // Move/Drag не трогают параметры — величину перемещения даёт только положение.
                        brief.Loc = _paramCache.DiffLocation(id.Value, LocationSignature.Of(element));
                    }
                }
                modified.Add(brief);
            }

            var deleted = new List<long>();
            foreach (var id in e.GetDeletedElementIds())
            {
                deleted.Add(id.Value);
                _paramCache.Remove(id.Value);
            }

            var record = new EventRecord
            {
                Kind = "doc_changed",
                Op = op.ToString(),
                Undone = false,
                Txn = txn,
                Cmd = _commands.TryCorrelate(_config.CommandCorrelationMs),
                View = TryActiveView(),
                Added = added,
                Modified = modified,
                Deleted = deleted,
            };

            switch (op)
            {
                case UndoOperation.TransactionUndone:
                    record.UndoneSeqs = _undo.OnUndone(txn, _log.Warn);
                    break;
                case UndoOperation.TransactionRedone:
                    record.RedoneSeqs = _undo.OnRedone(txn, _log.Warn);
                    break;
            }

            var seq = Emit(record);

            if (op == UndoOperation.TransactionCommitted)
                _undo.OnCommitted(seq, txn);
        }
        catch (Exception ex)
        {
            _log.Error("OnDocumentChanged", ex);
        }
    }

    private void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
    {
        try
        {
            var accessor = e.GetFailuresAccessor();
            if (!IsOurDoc(accessor.GetDocument())) return;

            var items = new List<FailureItem>();
            foreach (var message in accessor.GetFailureMessages())
            {
                var item = new FailureItem();
                try { item.Severity = message.GetSeverity().ToString(); } catch { }
                try { item.Description = message.GetDescriptionText(); } catch { }
                try { item.DefId = message.GetFailureDefinitionId().Guid.ToString(); } catch { }
                try { item.ElementIds = message.GetFailingElementIds().Select(i => i.Value).ToList(); } catch { }
                try { item.AdditionalIds = message.GetAdditionalElementIds().Select(i => i.Value).ToList(); } catch { }
                try
                {
                    if (message.HasResolutions())
                    {
                        item.Resolution = message.GetCurrentResolutionType().ToString();
                        item.Caption = message.GetDefaultResolutionCaption();
                    }
                }
                catch { }
                items.Add(item);
            }

            // FailuresProcessing приходит на КАЖДЫЙ коммит, в том числе без единой проблемы:
            // на живой сессии такие пустышки составили треть потока. Пишем только содержательные.
            if (items.Count == 0)
                return;

            var info = new FailuresInfo { Items = items };
            try { info.Severity = accessor.GetSeverity().ToString(); } catch { }
            try { info.Txn = accessor.GetTransactionName(); } catch { }
            try { info.Committing = accessor.IsTransactionBeingCommitted(); } catch { }
            try { info.Result = e.GetProcessingResult().ToString(); } catch { }

            Emit(new EventRecord { Kind = "failures", Failures = info, View = TryActiveView() });
        }
        catch (Exception ex)
        {
            _log.Error("OnFailuresProcessing", ex);
        }
    }

    private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        try
        {
            var dialog = new DialogInfo { DialogId = e.DialogId };
            switch (e)
            {
                case TaskDialogShowingEventArgs td:
                    dialog.Type = "TaskDialog";
                    dialog.Message = td.Message;
                    break;
                case MessageBoxShowingEventArgs mb:
                    dialog.Type = "MessageBox";
                    dialog.DialogType = mb.DialogType;
                    dialog.Message = mb.Message;
                    break;
                default:
                    dialog.Type = e.GetType().Name;
                    break;
            }

            Emit(new EventRecord { Kind = "dialog", Dialog = dialog });
        }
        catch (Exception ex)
        {
            _log.Error("OnDialogBoxShowing", ex);
        }
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        try
        {
            Emit(new EventRecord
            {
                Kind = "view_activated",
                Foreign = IsOurDoc(e.Document) ? null : true,
                View = ToViewRef(e.CurrentActiveView),
                PrevView = ToViewRef(e.PreviousActiveView),
            });
        }
        catch (Exception ex)
        {
            _log.Error("OnViewActivated", ex);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            var doc = e.GetDocument();
            if (!IsOurDoc(doc)) return;

            var ids = e.GetSelectedElements().Select(i => i.Value).ToList();
            var cats = new List<string>();
            foreach (var id in e.GetSelectedElements())
            {
                var cat = doc.GetElement(id)?.Category?.Name;
                if (cat is not null && !cats.Contains(cat)) cats.Add(cat);
            }

            Emit(new EventRecord
            {
                Kind = "selection_changed",
                Selection = new SelectionInfo { Count = ids.Count, Ids = ids, Cats = cats },
                View = TryActiveView(),
            });
        }
        catch (Exception ex)
        {
            _log.Error("OnSelectionChanged", ex);
        }
    }

    private void OnRibbonItemExecuted(object? sender, Autodesk.Internal.Windows.RibbonItemExecutedEventArgs e)
    {
        try
        {
            var item = e.Item;
            if (item is null) return;

            var command = new CommandRef
            {
                Id = string.IsNullOrEmpty(item.Id) ? item.UID : item.Id,
                Title = CleanTitle(item.AutomationName) ?? CleanTitle(item.Text) ?? item.Name,
            };

            _commands.Record(command);
            Emit(new EventRecord { Kind = "command", Cmd = command });
        }
        catch (Exception ex)
        {
            _log.Error("OnRibbonItemExecuted", ex);
        }
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e) =>
        EmitDocEvent("doc_opened", e.Document, status: e.Status.ToString());

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        EmitDocEvent("doc_closing", e.Document, status: null);

        // Закрытие записываемого документа завершает сессию без диалогов.
        try
        {
            if (IsOurDoc(e.Document) && IsActive)
            {
                // Снапшот «после» здесь не снимаем: документ закрывается, а долгая
                // операция в обработчике закрытия и ExportImage из события запрещены.
                _log.Info("recorded document is closing - auto-stop without after-snapshot");
                Stop(takeAfterSnapshot: false);
            }
        }
        catch (Exception ex)
        {
            _log.Error("OnDocumentClosing.AutoStop", ex);
        }
    }

    private void OnDocumentSaved(object? sender, DocumentSavedEventArgs e) =>
        EmitDocEvent("doc_saved", e.Document, status: e.Status.ToString());

    private void OnDocumentSavedAs(object? sender, DocumentSavedAsEventArgs e) =>
        EmitDocEvent("doc_saved_as", e.Document, status: e.Status.ToString(), doc =>
        {
            doc.OriginalPath = e.OriginalPath;
            doc.SavingAsCentral = e.IsSavingAsCentralFile;
        });

    private void OnDocumentSynchronized(object? sender, DocumentSynchronizedWithCentralEventArgs e) =>
        EmitDocEvent("doc_synced", e.Document, status: e.Status.ToString());

    private void OnDocumentPrinted(object? sender, DocumentPrintedEventArgs e) =>
        EmitDocEvent("doc_printed", e.Document, status: e.Status.ToString(), doc =>
        {
            var document = e.Document;
            if (document is null) return;
            doc.PrintedViews = e.GetPrintedViewElementIds()
                .Select(id => ToViewRef(document.GetElement(id) as View))
                .Where(v => v is not null).Select(v => v!).ToList();
            doc.FailedViews = e.GetFailedViewElementIds()
                .Select(id => ToViewRef(document.GetElement(id) as View))
                .Where(v => v is not null).Select(v => v!).ToList();
        });

    private void EmitDocEvent(string kind, Document? document, string? status, Action<DocInfo>? enrich = null)
    {
        try
        {
            var info = new DocInfo
            {
                Title = document?.Title,
                Path = document?.PathName,
                Status = status,
            };
            try { info.Workshared = document?.IsWorkshared; } catch { }
            enrich?.Invoke(info);

            Emit(new EventRecord
            {
                Kind = kind,
                Foreign = document is null || IsOurDoc(document) ? null : true,
                Doc = info,
            });
        }
        catch (Exception ex)
        {
            _log.Error($"EmitDocEvent.{kind}", ex);
        }
    }

    // ---- служебное --------------------------------------------------------

    private void EmitSessionStart()
    {
        var info = new DocInfo { Title = _doc.Title, Path = _doc.PathName };
        try { info.Workshared = _doc.IsWorkshared; } catch { }
        Emit(new EventRecord { Kind = "session_start", Doc = info, View = TryActiveView() });
    }

    private long Emit(EventRecord record)
    {
        record.SchemaVersion = Schema.Version;
        record.Seq = ++_seq;
        record.T = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
        _counts[record.Kind] = _counts.GetValueOrDefault(record.Kind) + 1;
        if (!_writer.TryWrite(record))
            _log.Warn($"event {record.Seq} ({record.Kind}) dropped: writer queue closed");
        return record.Seq;
    }

    private bool IsOurDoc(Document? document)
    {
        if (document is null) return false;
        if (ReferenceEquals(document, _doc)) return true;
        try
        {
            if (document.Equals(_doc)) return true;
            return string.Equals(document.Title, _doc.Title, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private ViewRef? TryActiveView()
    {
        try
        {
            var uidoc = _uiapp.ActiveUIDocument;
            if (uidoc is null || !IsOurDoc(uidoc.Document)) return null;
            return ToViewRef(uidoc.ActiveView);
        }
        catch
        {
            return null;
        }
    }

    private static ViewRef? ToViewRef(View? view)
    {
        if (view is null) return null;
        try
        {
            return new ViewRef { Id = view.Id.Value, Name = view.Name, Type = view.ViewType.ToString() };
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return result.Length == 0 ? "Model" : result;
    }

    private void WriteManifest(DateTimeOffset? finishedAt, int? undoneMarked)
    {
        try
        {
            var manifest = new SessionManifest
            {
                RevitVersion = _app.VersionNumber,
                RevitBuild = _app.VersionBuild,
                AddinVersion = typeof(RecordingSession).Assembly.GetName().Version?.ToString(),
                ModelTitle = _docTitle,
                ModelPath = _doc.PathName,
                RevitUser = _app.Username,
                WindowsUser = Environment.UserName,
                Machine = Environment.MachineName,
                StartedAt = _startedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture),
                FinishedAt = finishedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture),
                EventCounts = finishedAt is null ? null : new Dictionary<string, long>(_counts),
                UndoneMarked = undoneMarked,
            };
            try { manifest.Workshared = _doc.IsWorkshared; } catch { }

            File.WriteAllText(
                Path.Combine(Folder, "manifest.json"),
                JsonSerializer.Serialize(manifest, RarJson.Indented));
        }
        catch (Exception ex)
        {
            _log.Error("WriteManifest", ex);
        }
    }
}
