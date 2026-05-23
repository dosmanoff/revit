namespace RevitPlugin.Domain.Abstractions;

/// <summary>
/// Доступ к параметрам элементов Revit. Реализация адаптера автоматически
/// подгружает недостающие Shared Parameters из <c>SharedParameters.txt</c>.
/// </summary>
public interface IParameterStore
{
    void EnsureSharedParameter(string name, ParameterScope scope);
    void Set(long elementId, string paramName, object? value);
    string? GetString(long elementId, string paramName);
}

public enum ParameterScope
{
    Rebar,
    Wall,
    View,
    Sheet
}
