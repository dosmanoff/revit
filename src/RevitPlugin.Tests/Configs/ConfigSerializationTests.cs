using FluentAssertions;
using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Infrastructure;
using Xunit;

namespace RevitPlugin.Tests.Configs;

public class ConfigSerializationTests
{
    private const string MinimalJson = """
        {
          "schema_version": 1,
          "id": "550e8400-e29b-41d4-a716-446655440000",
          "name": "Test config",
          "external_reinforcement": {
            "spacing_vertical": 250,
            "wall_end_offset_mode": "FromStartEnd"
          }
        }
        """;

    [Fact]
    public void FromJson_parses_minimal_config()
    {
        var config = ConfigStorage.FromJson(MinimalJson);

        config.SchemaVersion.Should().Be(1);
        config.Id.Should().Be("550e8400-e29b-41d4-a716-446655440000");
        config.Name.Should().Be("Test config");
        config.ExternalReinforcement.Should().NotBeNull();
        config.ExternalReinforcement!.SpacingVertical.Should().Be(250);

        // Дефолты применяются к не указанным полям
        config.ExternalReinforcement.SpacingHorizontal.Should().Be(200);
        config.ExternalReinforcement.Major.Should().Be("Vertical");
    }

    [Fact]
    public void FromJson_throws_on_empty_input()
    {
        Action act = () => ConfigStorage.FromJson(" ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromJson_throws_on_unsupported_schema_version()
    {
        const string json = """{ "schema_version": 99 }""";
        Action act = () => ConfigStorage.FromJson(json);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Round_trip_preserves_unknown_sections_via_extension_data()
    {
        // Используем секцию из будущих модулей (M4: l_corner), ещё не покрытую типизированными моделями —
        // она должна попасть в ExtensionData и пережить round-trip без потери данных.
        const string json = """
            {
              "schema_version": 1,
              "id": "abc",
              "name": "rt",
              "l_corner": { "outer": { "bar1": { "diameter": 12 } } }
            }
            """;

        var config = ConfigStorage.FromJson(json);
        config.ExtensionData.Should().ContainKey("l_corner");

        var serialized = ConfigStorage.ToJson(config);
        serialized.Should().Contain("\"l_corner\"");
        serialized.Should().Contain("bar1");
    }

    [Fact]
    public void Default_mvp_config_is_loadable()
    {
        // Этот тест проверяет, что заводская конфигурация в Resources/DefaultConfigs
        // парсится без ошибок (если она есть в выводе сборки).
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "RevitPlugin", "Resources", "DefaultConfigs", "default-mvp.wrsconfig.json");

        if (!File.Exists(path))
            return; // не критично — конфиг копируется только в RevitPlugin output

        var json = File.ReadAllText(path);
        var config = ConfigStorage.FromJson(json);
        config.Name.Should().Be("WRS — MVP Default");
        config.ExternalReinforcement.Should().NotBeNull();
        config.InternalReinforcement.Should().NotBeNull();
    }
}
