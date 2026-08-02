using System.Text.RegularExpressions;

namespace RebarGeneration.Contracts;

/// <summary>
/// Формат стабильного ключа в параметре Comments — чистая, тестируемая часть
/// идемпотентности.
/// <para>
/// Формат намеренно совпадает с revit-context (<c>actions/keys.py</c>): токен
/// <c>[[ctx:KEY]]</c> вклеивается в Comments, НЕ затирая остальной текст. Это не
/// придирка: в Comments у арматуры живут марки вида «8#7 [1]», и затирание их
/// сломало бы спецификации. Совпадение формата важно ещё и потому, что модель
/// пишут два инструмента — плагин и мост; разойдись они в разборе ключа, и
/// идемпотентность развалится молча.
/// </para>
/// </summary>
public static class KeyToken
{
    private static readonly Regex TokenRe = new(@"\[\[ctx:([^\]]+)\]\]", RegexOptions.Compiled);

    public static string Format(string key) => $"[[ctx:{key}]]";

    /// <summary>Достать ключ из строки Comments, либо <c>null</c>.</summary>
    public static string? Parse(string? text)
    {
        Match m = TokenRe.Match(text ?? string.Empty);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static bool Contains(string? text) => TokenRe.IsMatch(text ?? string.Empty);

    /// <summary>
    /// Вклеить токен в существующий текст: имеющийся токен заменяется, обычный
    /// текст сохраняется, лишние пробелы не копятся.
    /// </summary>
    public static string Merge(string? existing, string key)
    {
        string token = Format(key);
        string text = existing ?? string.Empty;
        if (TokenRe.IsMatch(text)) return TokenRe.Replace(text, token);
        return text.Length == 0 ? token : text.TrimEnd() + " " + token;
    }
}
