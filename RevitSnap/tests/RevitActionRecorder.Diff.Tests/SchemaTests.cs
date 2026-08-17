using RevitActionRecorder.Model;
using Xunit;

namespace RevitActionRecorder.Diff.Tests;

public class SchemaTests
{
    [Fact]
    public void DiffEngine_supports_current_schema_version()
    {
        Assert.Equal(Schema.Version, DiffEngine.SupportedSchemaVersion);
    }
}
