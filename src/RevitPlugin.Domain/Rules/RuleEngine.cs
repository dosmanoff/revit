using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Reports;

namespace RevitPlugin.Domain.Rules;

/// <summary>
/// Прогоняет набор правил по списку стен и собирает <see cref="JobReport"/>.
/// Сам ничего не пишет в Revit — это работа оркестратора + <c>IRebarFactory</c>.
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;

    public RuleEngine(IEnumerable<IRule> rules) => _rules = rules.ToList();

    /// <summary>Дефолтный движок с правилами уровня M1 MVP (sequence важен — порядок Rule Id определяет порядок выполнения).</summary>
    public static RuleEngine CreateDefault() =>
        new(new IRule[]
        {
            new ExternalMeshRule(),
            new InternalMeshRule(),
            new PerimeterEdgeRule(),
            new OpeningEdgeRule()
        });

    public JobReport Run(IEnumerable<WallContext> walls, RebarConfig config, string jobId)
    {
        var results = new List<RuleResult>();
        var errors = new List<string>();

        foreach (var wall in walls)
        {
            foreach (var rule in _rules)
            {
                try
                {
                    if (rule.IsApplicable(wall, config))
                        results.Add(rule.Execute(wall, config));
                }
                catch (Exception ex)
                {
                    errors.Add($"Rule {rule.Id} failed on wall {wall.Id}: {ex.Message}");
                }
            }
        }

        return new JobReport(jobId, DateTimeOffset.UtcNow, results, errors);
    }
}
