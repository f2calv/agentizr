using CasCap.AgentRuntime.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace CasCap.IntegrationTests;

/// <summary>Credential-free smoke tests for the application host endpoints.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class EndpointSmokeTests(AgentizrWebApplicationFactory factory)
    : IClassFixture<AgentizrWebApplicationFactory>
{
    [Theory]
    [InlineData("/status")]
    [InlineData("/healthz")]
    public async Task GetEndpoint(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_IsAbsentByDefault()
    {
        using var client = factory.CreateClient();
        using var request = AgentizrMcpEndpointTests.CreateInitializeRequest();

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ApplicationApi_UsesControllersAndPreservesEndpointNames()
    {
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/')
                .StartsWith("api/v1/agents/", StringComparison.Ordinal) is true)
            .ToArray();
        string[] expectedNames =
        [
            "RunAgentV1", "StreamAgentV1", "GetAgentSessionV1", "ResetAgentSessionV1",
            "CompactAgentSessionV1", "SaveAgentSessionSnapshotV1", "LoadAgentSessionSnapshotV1",
            "DeleteAgentSessionSnapshotV1", "GetAgentOverridesV1", "SetAgentOverridesV1",
            "GetAgentInstructionsV1", "PublishAgentDefinitionV1", "GetAgentDefinitionHistoryV1",
            "GetActiveAgentDefinitionV1", "GetAgentDefinitionVersionV1", "EvaluateAgentDefinitionV1",
            "ActivateAgentDefinitionV1", "GetAgentDefinitionActivationHistoryV1", "DeactivateAgentDefinitionV1",
        ];

        Assert.Equal(expectedNames.Order(StringComparer.Ordinal),
            endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
                .Order(StringComparer.Ordinal));
        Assert.All(endpoints, endpoint =>
        {
            var action = Assert.IsType<ControllerActionDescriptor>(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
            Assert.Equal("CasCap.Controllers", action.ControllerTypeInfo.Namespace);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });
    }

    [Fact]
    public void ApplicationApi_InfersTypedResponseMetadata()
    {
        var descriptions = factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items)
            .Where(description => description.RelativePath?.StartsWith("api/v1/agents/", StringComparison.Ordinal) is true)
            .ToDictionary(description => description.ActionDescriptor.AttributeRouteInfo!.Name!);
        (string Name, int Status, Type Body)[] expectedResponses =
        [
            ("RunAgentV1", 200, typeof(RunAgentResponse)),
            ("StreamAgentV1", 200, typeof(IAsyncEnumerable<RunAgentStreamItem>)),
            ("GetAgentSessionV1", 200, typeof(AgentSessionInfoResponse)),
            ("ResetAgentSessionV1", 204, typeof(void)),
            ("CompactAgentSessionV1", 200, typeof(CompactAgentSessionResponse)),
            ("SaveAgentSessionSnapshotV1", 204, typeof(void)),
            ("LoadAgentSessionSnapshotV1", 204, typeof(void)),
            ("DeleteAgentSessionSnapshotV1", 204, typeof(void)),
            ("GetAgentOverridesV1", 200, typeof(AgentOverridesResponse)),
            ("SetAgentOverridesV1", 200, typeof(AgentOverridesResponse)),
            ("GetAgentInstructionsV1", 200, typeof(AgentInstructionsResponse)),
            ("PublishAgentDefinitionV1", 201, typeof(AgentDefinitionSnapshotResponse)),
            ("GetAgentDefinitionHistoryV1", 200, typeof(IReadOnlyList<AgentDefinitionHistoryItemResponse>)),
            ("GetActiveAgentDefinitionV1", 200, typeof(AgentDefinitionSnapshotResponse)),
            ("GetAgentDefinitionVersionV1", 200, typeof(AgentDefinitionSnapshotResponse)),
            ("EvaluateAgentDefinitionV1", 200, typeof(AgentEvaluationResponse)),
            ("ActivateAgentDefinitionV1", 204, typeof(void)),
            ("GetAgentDefinitionActivationHistoryV1", 200, typeof(IReadOnlyList<AgentDefinitionActivationResponse>)),
            ("DeactivateAgentDefinitionV1", 204, typeof(void)),
        ];
        string[] noNotFound =
        [
            "StreamAgentV1", "PublishAgentDefinitionV1", "GetAgentDefinitionHistoryV1",
            "GetAgentDefinitionActivationHistoryV1",
        ];
        string[] runtimeValidated = ["RunAgentV1", "StreamAgentV1", "CompactAgentSessionV1", "SetAgentOverridesV1"];
        string[] runtimeReadOnly = ["GetAgentSessionV1", "GetAgentOverridesV1", "GetAgentInstructionsV1"];

        Assert.Equal(expectedResponses.Length, descriptions.Count);
        foreach (var (name, status, body) in expectedResponses)
        {
            var responses = descriptions[name].SupportedResponseTypes;
            Assert.Equal(body, Assert.Single(responses, response => response.StatusCode == status).Type);
            var action = Assert.IsType<ControllerActionDescriptor>(descriptions[name].ActionDescriptor);
            var isRuntime = action.ControllerName == "AgentRuntime";
            if (!isRuntime || runtimeValidated.Contains(name))
                Assert.Equal(typeof(ValidationProblemDetails),
                    Assert.Single(responses, response => response.StatusCode == 400).Type);
            else
                Assert.DoesNotContain(responses, response => response.StatusCode == 400);
            if (!noNotFound.Contains(name))
                Assert.Contains(responses, response => response.StatusCode == 404);
            if (name is "PublishAgentDefinitionV1" or "ActivateAgentDefinitionV1" or "DeactivateAgentDefinitionV1")
                Assert.Contains(responses, response => response.StatusCode == 501);
            if (isRuntime && !runtimeReadOnly.Contains(name))
                Assert.Equal(typeof(ProblemDetails),
                    Assert.Single(responses, response => response.StatusCode == 409).Type);
            else if (isRuntime)
                Assert.DoesNotContain(responses, response => response.StatusCode == 409);
        }
        Assert.Contains(descriptions["PublishAgentDefinitionV1"].SupportedResponseTypes, response => response.StatusCode == 409);
    }
}
