using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Placement;

namespace RevitPlugin.Domain.Rules;

/// <summary>Сетка от внутренней грани целевого слоя стены.</summary>
public sealed class InternalMeshRule : MeshRuleBase
{
    public override string Id => "wrs.internal_mesh";
    protected override double FaceSign => -1;
    protected override MeshConfig? GetMesh(RebarConfig config) => config.InternalReinforcement;
    protected override BarRole VerticalRole => BarRole.Vertical;
    protected override BarRole HorizontalRole => BarRole.Horizontal;
}
