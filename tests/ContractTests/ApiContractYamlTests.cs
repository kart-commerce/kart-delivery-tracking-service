using FluentAssertions;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace KartDeliveryTrackingService.ContractTests;

/// <summary>
/// Sanity-checks the committed contracts/api-contract.yaml still declares the operations this
/// service's controllers actually implement - catches the contract and the code silently
/// drifting apart (contracts/README.md: "never hand-edited here", only re-copied from the
/// upstream design record).
/// </summary>
public class ApiContractYamlTests
{
    private static YamlMappingNode LoadContract()
    {
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "Fixtures", "api-contract.yaml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    [Fact]
    public void Contract_DeclaresGetTrackingStatusPath()
    {
        var root = LoadContract();
        var paths = (YamlMappingNode)root.Children[new YamlScalarNode("paths")];

        paths.Children.Should().ContainKey(new YamlScalarNode("/v1/tracking/{trackingId}"));
        var getOperation = (YamlMappingNode)((YamlMappingNode)paths.Children[new YamlScalarNode("/v1/tracking/{trackingId}")]).Children[new YamlScalarNode("get")];
        ((YamlScalarNode)getOperation.Children[new YamlScalarNode("operationId")]).Value.Should().Be("getTrackingStatus");

        var responses = (YamlMappingNode)getOperation.Children[new YamlScalarNode("responses")];
        responses.Children.Should().ContainKey(new YamlScalarNode("200"));
        responses.Children.Should().ContainKey(new YamlScalarNode("202"));
        responses.Children.Should().NotContainKey(new YamlScalarNode("404"), "ddd-model.md Modeling Decision 2 - this endpoint deliberately never returns 404");
    }

    [Fact]
    public void Contract_DeclaresIngestCarrierWebhookPath()
    {
        var root = LoadContract();
        var paths = (YamlMappingNode)root.Children[new YamlScalarNode("paths")];

        paths.Children.Should().ContainKey(new YamlScalarNode("/internal/v1/webhooks/carriers/{carrierId}"));
        var postOperation = (YamlMappingNode)((YamlMappingNode)paths.Children[new YamlScalarNode("/internal/v1/webhooks/carriers/{carrierId}")]).Children[new YamlScalarNode("post")];
        ((YamlScalarNode)postOperation.Children[new YamlScalarNode("operationId")]).Value.Should().Be("ingestCarrierWebhook");

        var responses = (YamlMappingNode)postOperation.Children[new YamlScalarNode("responses")];
        foreach (var expected in new[] { "200", "401", "404", "503" })
        {
            responses.Children.Should().ContainKey(new YamlScalarNode(expected));
        }
    }

    [Fact]
    public void Contract_CanonicalDeliveryStatusEnum_MatchesDomainEnumExactly()
    {
        var root = LoadContract();
        var schemas = (YamlMappingNode)((YamlMappingNode)root.Children[new YamlScalarNode("components")]).Children[new YamlScalarNode("schemas")];
        var statusSchema = (YamlMappingNode)schemas.Children[new YamlScalarNode("CanonicalDeliveryStatus")];
        var enumNode = (YamlSequenceNode)statusSchema.Children[new YamlScalarNode("enum")];

        var contractValues = enumNode.Children.Select(n => ((YamlScalarNode)n).Value).ToArray();
        var domainValues = Enum.GetNames<KartDeliveryTrackingService.Domain.Tracking.CanonicalDeliveryStatus>();

        contractValues.Should().BeEquivalentTo(domainValues, "the wire enum and the domain enum must never silently drift apart");
    }
}
