using System.Text.Json;
using RebarGeneration.Contracts;

// UseWPF заменяет стандартный набор implicit usings своим, System.IO в него не
// входит, а System.Windows.Shapes (входит) приносит собственный Path — поэтому
// импорт здесь явный и с алиасами.
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace RebarGeneration.Core;

/// <summary>
/// Журнал прогонов — append-only JSONL, одна строка на прогон.
/// <para>
/// Отличие от журнала revit-context намеренное: там на каждую мутацию читался и
/// переписывался ВЕСЬ JSON-файл, что на тысяче стержней даёт квадратичный объём
/// записи. Здесь единица журналирования — прогон, а не элемент, и запись
/// добавляется в конец файла.
/// </para>
/// <para>
/// Журнал не является механизмом отката: откат пакета — это один шаг Revit Undo,
/// потому что весь прогон лежит в одном <c>TransactionGroup</c>. Журнал нужен для
/// ответа на вопрос «что и когда положили в модель».
/// </para>
/// </summary>
public static class RunJournal
{
    public static string Folder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitPlugin", "RebarGeneration");

    public static string PathFor(string? documentTitle)
    {
        string safe = Sanitize(documentTitle ?? "doc");
        return Path.Combine(Folder, $"runs-{safe}.jsonl");
    }

    /// <summary>Дописать строку о прогоне. Журнал никогда не должен ронять прогон,
    /// поэтому ошибки записи проглатываются и возвращаются как <c>null</c>.</summary>
    public static string? Append(string? documentTitle, RunReport report, string? jobPath)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(documentTitle);

            var line = new
            {
                ts = DateTime.UtcNow.ToString("O"),
                mode = report.Mode,
                job = jobPath,
                elapsedMs = report.ElapsedMs,
                transactions = report.Transactions,
                counts = report.Counts,
                totalLengthFt = Math.Round(report.TotalLengthFt, 3),
                mapFile = report.MapFile,
                errors = report.Errors.Count,
                keys = report.Groups.ConvertAll(g => g.Key),
            };

            File.AppendAllText(path, JsonSerializer.Serialize(line) + Environment.NewLine);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string Sanitize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = char.IsLetterOrDigit(s[i]) || s[i] is '-' or '.' ? s[i] : '_';
        return new string(buf);
    }
}
