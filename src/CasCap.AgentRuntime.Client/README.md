# CasCap.AgentRuntime.Client

Typed HTTP execution and definition-administration clients for .NET 10 applications using the
CasCap multi-tenant Agent Runtime. The application owns authentication, resilience and presentation;
the runtime owns agent execution and session state.

## Installation

```powershell
dotnet package add CasCap.AgentRuntime.Client
```

The package targets `net10.0` and depends on `CasCap.AgentRuntime.Contracts`.

## Usage

In an existing Generic Host application (with `Microsoft.Extensions.Hosting` referenced), use the
connection options below in the application's configuration:

```csharp
using CasCap.AgentRuntime.Client.Abstractions;
using CasCap.AgentRuntime.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAgentRuntimeClient();
using var host = builder.Build();

var client = host.Services.GetRequiredService<IAgentRuntimeClient>();
var response = await client.RunAgentAsync(
    "assistant",
    new RunAgentRequest
    {
        SessionId = "conversation-example",
        Input = "Summarize the available capabilities.",
    },
    CancellationToken.None);
Console.WriteLine(response?.OutputText);
```

Add a caller-owned authentication handler to the returned `IHttpClientBuilder` for authenticated
deployments. The token needs `agent.execute` and its application identity must map to a runtime
tenant. The snippet does not acquire a token.

## Configuration Examples

Minimal connection settings:

```json
{
  "CasCap": {
    "AgentRuntimeClientOptions": {
      "BaseAddress": "https://runtime.example.com/"
    }
  }
}
```

With an explicit timeout:

```json
{
  "CasCap": {
    "AgentRuntimeClientOptions": {
      "BaseAddress": "https://runtime.example.com/",
      "TimeoutMinutes": 15
    }
  }
}
```

`BaseAddress` is required. `TimeoutMinutes` defaults to `10` and accepts `1`–`60`. Both client
registrations bind and validate these same options at startup.

## Execution and Sessions

Register [`IAgentRuntimeClient`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Client/Abstractions/IAgentRuntimeClient.cs)
with `AddAgentRuntimeClient()` and configure
`CasCap:AgentRuntimeClientOptions:BaseAddress` and optional `TimeoutMinutes`. The registration returns
an `IHttpClientBuilder`; authentication and resilience belong to the consuming application and are
attached through that pipeline.

`RunAgentAsync` returns `null` when the named agent is unavailable to the authenticated tenant and
throws for other non-success responses. Session methods inspect/reset/compact active state, manage
named snapshots, and get or replace complete per-session runtime overrides without exposing raw
session JSON. `GetInstructionsAsync` returns the effective instructions, or `null` when the agent is
unavailable or its tenant has not enabled instructions disclosure.

`StreamAgentAsync` incrementally yields live delegation/compaction events and then the final response
using a streamed JSON response. Callers opt into tool arguments and property bags through
`RunAgentRequest.IncludeDiagnosticDetails` only when their diagnostic destination is operator-controlled.
Unlike `RunAgentAsync`, streaming throws on any non-success response, including 404. The stream
is a JSON array consumed incrementally, not Server-Sent Events.

## Definition Administration

Register [`IAgentDefinitionAdminClient`](https://github.com/f2calv/agentizr/blob/13aa4a19180b9dbd0f14fa08fbf95f67039b69f0/src/CasCap.AgentRuntime.Client/Abstractions/IAgentDefinitionAdminClient.cs)
separately with `AddAgentDefinitionAdminClient()`. It publishes
inactive immutable snapshots, reads active/version/history state, activates or rolls back versions,
evaluates fixture scenarios, deactivates active definitions, and reads activation audit history.
Attach control-plane credentials carrying definition scopes to that client; execution workloads
need not receive those permissions.

| Operation | Permission |
| --- | --- |
| Snapshot and history reads | `agent.definition.read` |
| Publish | `agent.definition.publish` |
| Evaluate, activate, roll back or deactivate | `agent.definition.activate` |

The server can require a passing evaluation before activation. Deactivation requires a reason and
is refused while another active agent delegates to the target.

## Documentation

- [Runtime source and project catalogue](https://github.com/f2calv/agentizr)
- [Host APIs and configuration](https://github.com/f2calv/agentizr/blob/main/src/CasCap.App.Server/README.md)
- [License](https://github.com/f2calv/agentizr/blob/main/LICENSE)
