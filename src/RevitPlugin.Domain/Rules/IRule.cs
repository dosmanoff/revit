using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Reports;

namespace RevitPlugin.Domain.Rules;

/// <summary>
/// Атомарное правило армирования. Принимает контекст стены + конфиг,
/// возвращает набор <c>RebarPlacement</c> + предупреждения.
/// Реализация не должна обращаться к Revit API.
/// </summary>
public interface IRule
{
    /// <summary>Стабильный идентификатор правила, например <c>wrs.external_mesh</c>.</summary>
    string Id { get; }

    /// <summary>Применимо ли правило к данной стене с данным конфигом.</summary>
    bool IsApplicable(WallContext wall, RebarConfig config);

    RuleResult Execute(WallContext wall, RebarConfig config);
}
