# Agent Runtime Grafana dashboards

Publishes the tenant-scoped Agent Runtime execution dashboard as a sidecar-discoverable ConfigMap.
The [`agentizr`](../agentizr/README.md) application chart bundles it as a `file://` subchart enabled by
`dashboards.enabled`; its version stays fixed at `0.1.0`. Set the values below under the application
chart's `dashboards` key.

## Configuration

| Value | Default | Notes |
| --- | --- | --- |
| `enabled` | `true` | Set `false` to render no dashboard ConfigMaps |
| `dashboardFolder` | `Agent Runtime` | Grafana sidecar folder annotation |
| `datasources.prometheus` | `prometheus` | Prometheus datasource UID |

Each JSON file under `dashboards/` becomes a ConfigMap labelled `grafana_dashboard: "1"`.
Datasource substitution is restricted to the chart's Prometheus placeholder so Grafana legend
tokens remain unchanged.

## Dashboard

| File | Title | Scope |
| --- | --- | --- |
| `agentizr-runtime.json` | Agent Runtime | Runs, outcomes, latency, first-token latency, tokens, and tool calls by tenant and active definition |

## Metric Contract

The dashboard consumes these OpenTelemetry instruments after Prometheus translation:

| Prometheus metric | Dimensions |
| --- | --- |
| `agentizr_run_total` | `tenant`, `agent`, `provider`, `model`, `outcome` |
| `agentizr_run_duration_milliseconds_bucket` | Run dimensions plus `le` |
| `agentizr_run_time_to_first_token_milliseconds_bucket` | Run dimensions plus `le` |
| `agentizr_tokens_total` | Run dimensions plus `token_kind` |
| `agentizr_tool_calls_total` | Run dimensions |

Provider and model labels are absent when a requested definition is unavailable. Labels contain
configuration names only; session identifiers, prompts, outputs, tool arguments, and caller IDs are
never exported.
