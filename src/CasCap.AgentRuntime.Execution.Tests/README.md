# CasCap.AgentRuntime.Execution.Tests

Unit and opt-in provider integration tests for
[CasCap.AgentRuntime.Execution](../CasCap.AgentRuntime.Execution/README.md) and
[CasCap.AgentRuntime.Evaluation](../CasCap.AgentRuntime.Evaluation/README.md).

## Running

The repository uses the .NET 10 native Microsoft.Testing.Platform runner. Debug builds need an
adjacent `CasCap.Common` checkout; Release uses published dependencies. From the repository root,
when test execution is intended:

```powershell
dotnet test --project src\CasCap.AgentRuntime.Execution.Tests\CasCap.AgentRuntime.Execution.Tests.csproj --configuration Release
```

The local provider categories stay skipped unless explicitly enabled. No tests were executed to
produce the inventory below; counts come from attributes and expanded `[InlineData]` rows.

## Tests

| Class | Methods | Cases | Covers |
| --- | --- | --- | --- |
| `ToolOutputStrippingChatReducerTests` | 15 | 20 | Tool-content stripping, orphaned tool-call prevention, sliding window, trigger threshold, system-message retention, metadata preservation, input immutability, argument validation |
| `SessionCompactionConfigTests` | 1 | 6 | Compaction notification modes |
| `ReasoningMarkupFilterTests` | 3 | 8 | Complete, orphaned-closing and unclosed `<think>` markup, clean text and reasoning-only answers |
| `AgentExtensionsCreateAgentTests` | 10 | 13 | Provider validation for Ollama / Azure OpenAI / OpenAI endpoints and credentials, caller-supplied transport, unsupported provider types |
| `ResponsesAgentTests` | 6 | 12 | Offline SDK/framework PDF attachment, strict schema, output ceilings, non-storage, Entra audience, pre-validation response/usage receipts, incomplete/malformed output, cancellation and no automatic paid-POST retries |
| `ToolSourceValidationTests` | 2 | 2 | Remote MCP logical credential references are valid only with endpoint sources |
| `ToolFilterTests` | 3 | 4 | Include-list filtering always rejects unresolved tool names, regardless of hosting environment |
| `ToolFailureTests` | 5 | 8 | Throwing tools and MCP failures, awaited tool activity/results with safe exception metadata, and budget/activity-sink sharing with sub-agents |
| `AgentResponseUsageTests` | 6 | 6 | Framework usage aggregation across tool-call round-trips, `RunAnalysisAsync` usage/tool-call reporting and leaked reasoning-markup removal |
| `AgentTelemetryTests` | 3 | 3 | Agent-level OpenTelemetry spans, sub-agent span nesting, sensitive-data opt-in |
| `AgentRunScopeTests` | 9 | 11 | Depth nesting, activity callback inheritance without inheriting the root structured-response observer, shared/thread-safe attachments, drain semantics and scoped tool failures |
| `AgentTypeRegistryTests` | 11 | 11 | Registry lookup, ambiguous-name rejection, DI and assembly indexing, tool resolution via registry |
| `AgentEvaluationGradingTests` | 7 | 18 | Number, phrase, anchored-pattern and combined answer checks, required/forbidden/side-effect grading, Wilson intervals |
| `AgentEvaluationToolTests` | 7 | 10 | Tool classification and filtering, fixture and sandbox responses, variant descriptions and schemas |
| `AgentEvaluationReportTests` | 5 | 7 | Per-cell and cross-model summaries, speed ratios, session files, visible-thinking detection |
| `LlamaCppIntegrationTests` | 4 | 4 | Opt-in real text, streaming, multimodal, and framework-session inference against local llama.cpp |
| `SessionLatencyBenchmarkTests` | 2 | 2 | Opt-in time-to-first-token, total latency and input tokens by session size for llama.cpp and Azure OpenAI, uncompacted versus reduced history |
| **Total** | **99** | **145** | Source-declared cases |

## Trait Categories

| Category | Applied to |
| --- | --- |
| `Chat Reduction` | `ToolOutputStrippingChatReducerTests`, `SessionCompactionConfigTests` |
| `Agent Creation` | `AgentExtensionsCreateAgentTests`, `ToolSourceValidationTests`, `ToolFilterTests` |
| `Structured Generation` | `ResponsesAgentTests` |
| `Tool Failures` | `ToolFailureTests` |
| `Usage Reporting` | `AgentResponseUsageTests` |
| `Reasoning Markup` | `ReasoningMarkupFilterTests` |
| `Telemetry` | `AgentTelemetryTests` |
| `Agent Run Scope` | `AgentRunScopeTests` |
| `Type Resolution` | `AgentTypeRegistryTests` |
| `Agent Evaluation` | `AgentEvaluationGradingTests`, `AgentEvaluationToolTests`, `AgentEvaluationReportTests` |
| `Integration`, `Local llama.cpp` | `LlamaCppIntegrationTests` |
| `Integration`, `Session Latency` | `SessionLatencyBenchmarkTests` |

## Skipped Tests

| Conditional skip reason | Cases affected |
| --- | ---: |
| Local llama.cpp tests require `CASCAP_RUN_LOCAL_LLAMA_CPP_TESTS=true` outside CI | 4 |
| Once enabled, missing/invalid local llama.cpp endpoint or model settings | 4 (same cases) |
| Once configured, missing `CASCAP_LOCAL_LLAMA_CPP_VISION_MODEL` | 1 (same image case) |
| Session latency benchmarks require `CASCAP_RUN_SESSION_LATENCY_BENCHMARKS=true` outside CI | 2 |
| Once enabled, missing llama.cpp settings | 1 (same benchmark case) |
| Once enabled, missing Azure OpenAI endpoint/deployment settings | 1 (same benchmark case) |

The vision case sends a synthetic image and requires the selected server's multimodal support.
Counts above overlap; they describe six conditionally skipped cases, not an observed run.

## Local llama.cpp Tests

These tests call a real OpenAI-compatible llama.cpp endpoint. They never run in CI and require an
explicit opt-in plus local environment configuration:

```powershell
$env:CASCAP_RUN_LOCAL_LLAMA_CPP_TESTS = 'true'
$env:CASCAP_LOCAL_LLAMA_CPP_ENDPOINT = 'http://localhost:8080'
$env:CASCAP_LOCAL_LLAMA_CPP_MODEL = '<loaded-model-name>'
$env:CASCAP_LOCAL_LLAMA_CPP_VISION_MODEL = '<loaded-vision-model-name>' # Optional
dotnet test --project src/CasCap.AgentRuntime.Execution.Tests/CasCap.AgentRuntime.Execution.Tests.csproj --filter-trait 'Category=Local llama.cpp'
```

`CASCAP_LOCAL_LLAMA_CPP_API_KEY` defaults to `sk-no-key-required`.
`CASCAP_LOCAL_LLAMA_CPP_TIMEOUT_SECONDS` defaults to 120 seconds. Local endpoints, model names and
credentials remain environment-only. When the endpoint is behind Basic auth, set both
`CASCAP_LOCAL_LLAMA_CPP_BASIC_AUTH_USERNAME` and `CASCAP_LOCAL_LLAMA_CPP_BASIC_AUTH_PASSWORD`.

## Session Latency Benchmarks

`SessionLatencyBenchmarkTests` measure how session size affects provider latency. Each turn of the
synthetic history holds a user question, a status tool call, a JSON tool result of about 1 KB and an
assistant answer. Every turn count is measured twice:

- **Uncompacted**: the full history, including tool calls and results.
- **Reduced**: the history after `ToolOutputStrippingChatReducer`, with the target and trigger both
  set to `CASCAP_SESSION_LATENCY_REDUCER_TARGET`.

The [session compaction guide](../../docs/session-compaction.md) records the results.

Each sample starts with a unique system-message nonce, so the figures are uncached prefill costs for
both llama.cpp and Azure OpenAI. llama.cpp output is capped at 64 tokens so prompt processing
dominates. Azure OpenAI uses low reasoning effort and a 1,024-token cap, because hidden reasoning
counts against the output budget. First-token time includes streamed reasoning where the provider
exposes it. The only assertion beyond streamed content is that reduced history uses fewer input
tokens than uncompacted history when the provider reports usage.

The results table, with median and range per cell, is written to the test output. Capture it with
an xUnit TRX report:

```powershell
$env:CASCAP_RUN_SESSION_LATENCY_BENCHMARKS = 'true'
# llama.cpp uses the CASCAP_LOCAL_LLAMA_CPP_* variables above.
$env:CASCAP_LOCAL_AZURE_OPENAI_ENDPOINT = 'https://<resource>.openai.azure.com'
$env:CASCAP_LOCAL_AZURE_OPENAI_DEPLOYMENT = '<deployment-name>'
dotnet test --project src/CasCap.AgentRuntime.Execution.Tests/CasCap.AgentRuntime.Execution.Tests.csproj --filter-trait 'Category=Session Latency' --report-xunit-trx --results-directory ./TestResults
```

Each provider test is skipped when its endpoint settings are absent. Optional settings:

| Variable | Default | Purpose |
| --- | --- | --- |
| `CASCAP_SESSION_LATENCY_TURNS` | `0,5,10,20` | Comma-separated turn counts |
| `CASCAP_SESSION_LATENCY_SAMPLES` | `3` | Samples per turn count and history shape |
| `CASCAP_SESSION_LATENCY_REDUCER_TARGET` | `20` | Reducer target message count |
| `CASCAP_LOCAL_AZURE_OPENAI_TIMEOUT_SECONDS` | `120` | Azure OpenAI request timeout |
| `CASCAP_LOCAL_AZURE_TENANT_ID` | Unset | Service principal tenant |
| `CASCAP_LOCAL_AZURE_CLIENT_ID` | Unset | Service principal application |
| `CASCAP_LOCAL_AZURE_CERT_THUMBPRINT` | Unset | Service principal certificate in the current user store |

Azure OpenAI authenticates with the certificate service principal when all three service principal
settings are set, and otherwise with the signed-in Azure CLI account. The identity needs a
data-plane role, such as Cognitive Services OpenAI User, on the resource. Large uncompacted turn
counts can exceed a local model's context window.

## File Structure

```text
Tests/
├── AgentEvaluationTestData.cs
├── Integration/
│   ├── LlamaCppIntegrationTests.cs
│   ├── LocalAzureOpenAiTestSettings.cs
│   ├── LocalLlamaCppFactAttribute.cs
│   ├── LocalLlamaCppTestEnvironment.cs
│   ├── LocalLlamaCppTestSettings.cs
│   ├── SessionLatencyBenchmarkEnvironment.cs
│   ├── SessionLatencyBenchmarkFactAttribute.cs
│   └── SessionLatencyBenchmarkTests.cs
└── Unit/
    ├── AgentEvaluationGradingTests.cs
    ├── AgentEvaluationReportTests.cs
    ├── AgentEvaluationToolTests.cs
    ├── AgentExtensionsCreateAgentTests.cs
    ├── AgentRunScopeTests.cs
    ├── AgentResponseUsageTests.cs
    ├── AgentTelemetryTests.cs
    ├── AgentTypeRegistryTests.cs
    ├── ReasoningMarkupFilterTests.cs
    ├── SessionCompactionConfigTests.cs
    ├── ToolFailureTests.cs
    ├── ToolFilterTests.cs
    ├── ToolOutputStrippingChatReducerTests.cs
    └── ToolSourceValidationTests.cs
```

## Notes

`ReduceAsync_MixedTextAndToolCall_NoOrphanedToolCall` verifies that mixed assistant text/tool
messages retain their text without orphaned calls. OpenAI-compatible APIs can reject calls
without matching results.

`AgentResponseUsageTests` pins the framework's aggregation of per-round-trip usage into
`AgentResponse.Usage`, which runtime reporting depends on.
