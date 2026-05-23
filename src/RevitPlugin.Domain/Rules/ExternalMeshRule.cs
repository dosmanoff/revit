using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Placement;

namespace RevitPlugin.Domain.Rules;

/// <summary>Сетка от внешней грани целевого слоя стены.</summary>
public sealed class ExternalMeshRule : MeshRuleBase
{
    public override string Id => "wrs.external_mesh";
    protected override double FaceSign => +1;
    protected override MeshConfig? GetMesh(RebarConfig config) => config.ExternalReinforcement;
    protected override BarRole VerticalRole => BarRole.Vertical;
    protected override BarRole HorizontalRole => BarRole.Horizontal;
}
