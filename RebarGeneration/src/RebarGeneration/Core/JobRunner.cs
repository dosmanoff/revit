using System.Diagnostics;
using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

// UseWPF заменяет стандартный набор implicit usings: System.IO в нём нет, а
// System.Windows.Shapes приносит собственный Path — импорт явный, с алиасом.
using Path = System.IO.Path;

namespace RebarGeneration.Core;

/// <summary>
/// Исполнитель задания: разворачивает группы в наборы и кладёт их в модель.
/// <para><b>Транзакции.</b> Весь прогон — один <see cref="TransactionGroup"/>, то
/// есть один шаг Revit Undo. Внутри — по одной <see cref="Transaction"/> на ХОСТ.
/// Критерий дробления именно хост, а не количество: так падение одного элемента
/// откатывает только его армирование, а не весь прогон. Счётчик
/// <c>policy.maxSetsPerTransaction</c> — вторичный предохранитель от разбухания
/// буфера отмены, а не основной механизм.</para>
/// <para><b>Отказ группы</b> не рвёт транзакцию хоста: уже созданные этой группой
/// элементы удаляются, группа помечается <c>failed</c>, остальные продолжают.
/// Частично собранная группа хуже отсутствующей — её нельзя оставлять.</para>
/// </summary>
public sealed class JobRunner(Document doc)
{
    private readonly Document _doc = doc;

    public RunReport Run(Job job, string? jobPath = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new RunReport
        {
            Mode = job.DryRun ? "dry-run" : "apply",
            Document = SafeTitle(),
        };

        IReadOnlyList<ReportError> invalid = JobValidator.Validate(job);
        if (invalid.Count > 0)
        {
            report.Ok = false;
            report.Errors.AddRange(invalid);
            report.ElapsedMs = sw.ElapsedMilliseconds;
            return report;
        }

        if (!string.IsNullOrWhiteSpace(job.Document) && !DocumentMatches(job.Document))
        {
            report.Ok = false;
            report.Errors.Add(new ReportError
            {
                Code = "WRONG_DOCUMENT",
                Message = $"задание составлено для '{job.Document}', а открыт '{SafeTitle()}'",
            });
            report.ElapsedMs = sw.ElapsedMilliseconds;
            return report;
        }

        var units = new LengthUnits.Converter(job.Units);
        var types = new TypeCache(_doc);
        var keys = KeyIndex.Build(_doc, BuiltInCategory.OST_Rebar);
        var hosts = new HostResolver(_doc);
        var factory = new RebarSetFactory(_doc, types, job.Policy.ReuseStandardShapes);
        var swallower = new WarningSwallower();
        var map = new Dictionary<string, long>(StringComparer.Ordinal);

        // Сгруппировать по хосту, сохранив порядок задания: транзакция на хост.
        var byHost = new List<(Element Host, List<GroupSpec> Groups)>();
        var hostSlot = new Dictionary<long, int>();

        foreach (GroupSpec g in job.Groups)
        {
            Element host;
            try
            {
                host = hosts.Resolve(g);
            }
            catch (JobException ex)
            {
                report.Groups.Add(Failed(g, ex.Message));
                report.Counts.Failed++;
                report.Errors.Add(new ReportError { Code = ex.Code, Key = g.Key, Message = ex.Message });
                continue;
            }

            long hk = HostResolver.TransactionKey(host);
            if (!hostSlot.TryGetValue(hk, out int slot))
            {
                slot = byHost.Count;
                hostSlot[hk] = slot;
                byHost.Add((host, []));
            }
            byHost[slot].Groups.Add(g);
        }

        using var group = new TransactionGroup(_doc, "RebarGeneration");
        group.Start();

        foreach ((Element host, List<GroupSpec> groups) in byHost)
        {
            foreach (List<GroupSpec> chunk in Chunk(groups, job.Policy.MaxSetsPerTransaction))
            {
                using var tx = new Transaction(_doc, $"Rebar {DescribeHost(host)}");
                if (job.Policy.SuppressWarnings) swallower.AttachTo(tx);
                tx.Start();
                report.Transactions++;

                foreach (GroupSpec g in chunk)
                    RunGroup(g, host, job, units, types, keys, factory, map, report);

                tx.Commit();
            }
        }

        // Сухой прогон честно строит всё и откатывает: отчёт настоящий, модель чистая.
        if (job.DryRun) group.RollBack(); else group.Assimilate();

        report.Counts.Groups = report.Groups.Count;
        report.Warnings.AddRange(swallower.ToReportLines());
        report.Warnings.AddRange(factory.LayoutRejections);

        foreach (string ambiguous in keys.AmbiguousKeys)
            report.Warnings.Add($"ключ '{ambiguous}' носят несколько элементов модели");

        if (!job.DryRun && map.Count > 0)
        {
            try
            {
                report.MapFile = JobIo.WriteMap(job.Policy.MapFile, jobPath, map);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"карта key→id не записана: {ex.Message}");
            }
        }

        report.Ok = report.Counts.Failed == 0 && report.Errors.Count == 0;
        report.ElapsedMs = sw.ElapsedMilliseconds;
        RunJournal.Append(report.Document, report, jobPath);
        return report;
    }

    // ------------------------------------------------------------------ группа

    private void RunGroup(
        GroupSpec g, Element host, Job job, LengthUnits.Converter units, TypeCache types,
        KeyIndex keys, RebarSetFactory factory, Dictionary<string, long> map, RunReport report)
    {
        // Ключ хранится рядом с id: группа может дать несколько наборов, и при
        // откате надо снять из индекса именно их ключи, а не ключ группы.
        var created = new List<(string Key, ElementId Id)>();
        var line = new GroupReport
        {
            Key = g.Key,
            Generator = g.Generator,
            HostId = host.Id.Value,
        };

        try
        {
            if (keys.IsAmbiguous(g.Key))
                throw new JobException("AMBIGUOUS_KEY",
                    $"{g.Key}: ключ носят несколько элементов; переименуй дубли и повтори");

            bool exists = keys.TryGet(g.Key, out ElementId existing);
            string policy = (job.Policy.OnExistingKey ?? "skip").ToLowerInvariant();

            if (exists)
            {
                switch (policy)
                {
                    case "skip":
                        line.Status = "skipped";
                        line.Reason = $"ключ уже существует (элемент {existing.Value})";
                        report.Counts.Skipped++;
                        report.Groups.Add(line);
                        return;
                    case "error":
                        throw new JobException("KEY_EXISTS", $"{g.Key}: ключ уже существует");
                    case "replace":
                        _doc.Delete(existing);
                        keys.Remove(g.Key);
                        report.Counts.Replaced++;
                        line.Status = "replaced";
                        break;
                }
            }

            var ctx = new BuildContext
            {
                Doc = _doc,
                Host = host,
                U = units,
                Defaults = job.Defaults,
                Types = types,
            };

            IReadOnlyList<PlannedSet> sets = GeneratorRegistry.Get(g.Generator).Plan(g, ctx);
            if (sets.Count == 0)
                throw new JobException("EMPTY_PLAN", $"{g.Key}: генератор не вернул ни одного набора");

            for (int i = 0; i < sets.Count; i++)
            {
                PlannedSet s = sets[i];
                // Составной ключ: группа может дать несколько наборов (например
                // сетка X + сетка Y), и каждый физический элемент обязан иметь
                // собственный ключ, иначе индекс станет неоднозначным.
                string key = sets.Count == 1 ? g.Key : $"{g.Key}:{s.Suffix ?? i.ToString()}";

                var rebar = factory.Create(s, host, key);
                created.Add((key, rebar.Id));
                keys.Add(key, rebar.Id);
                map[key] = rebar.Id.Value;

                report.Warnings.AddRange(factory.ApplyParams(rebar, g.Params, key));

                line.Sets++;
                line.Bars += s.Bars;
                line.LengthFt += s.BarLengthFt * s.Bars;
            }

            if (line.Status != "replaced") line.Status = "created";
            if (job.Policy.IncludeIds) line.ElementIds = created.ConvertAll(c => c.Id.Value);

            report.Counts.Sets += line.Sets;
            report.Counts.Bars += line.Bars;
            report.TotalLengthFt += line.LengthFt;
        }
        catch (Exception ex)
        {
            // Частично собранная группа хуже отсутствующей — сносим её след.
            foreach ((string key, ElementId id) in created)
            {
                try { _doc.Delete(id); } catch { /* элемент мог не создаться */ }
                keys.Remove(key);
                map.Remove(key);
            }

            string code = ex is JobException je ? je.Code : "GROUP_FAILED";
            line.Status = "failed";
            line.Reason = ex.Message;
            line.Sets = 0;
            line.Bars = 0;
            line.LengthFt = 0;
            report.Counts.Failed++;
            report.Errors.Add(new ReportError { Code = code, Key = g.Key, Message = ex.Message });
        }

        report.Groups.Add(line);
    }

    // ----------------------------------------------------------------- утилиты

    private static GroupReport Failed(GroupSpec g, string reason) => new()
    {
        Key = g.Key,
        Generator = g.Generator,
        Status = "failed",
        Reason = reason,
    };

    private static IEnumerable<List<GroupSpec>> Chunk(List<GroupSpec> groups, int max)
    {
        if (max <= 0 || groups.Count <= max)
        {
            yield return groups;
            yield break;
        }
        for (int i = 0; i < groups.Count; i += max)
            yield return groups.GetRange(i, Math.Min(max, groups.Count - i));
    }

    private static string DescribeHost(Element host)
    {
        string? name = null;
        try { name = host.Name; } catch { /* не у всех элементов есть Name */ }
        return string.IsNullOrWhiteSpace(name) ? host.Id.Value.ToString() : name;
    }

    private string SafeTitle()
    {
        try { return _doc.Title; } catch { return "doc"; }
    }

    private bool DocumentMatches(string wanted)
    {
        string w = Normalize(wanted);
        return Normalize(SafeTitle()) == w || Normalize(SafePath()) == w
               || Normalize(Path.GetFileNameWithoutExtension(SafePath())) == w;

        static string Normalize(string s)
        {
            s = (s ?? string.Empty).Trim().ToLowerInvariant();
            return s.EndsWith(".rvt", StringComparison.Ordinal) ? s[..^4] : s;
        }
    }

    private string SafePath()
    {
        try { return _doc.PathName ?? string.Empty; } catch { return string.Empty; }
    }
}
