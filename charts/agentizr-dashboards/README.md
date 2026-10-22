# agentizr-dashboards

Helm chart providing the tenant-scoped Agentizr execution and HA dashboard as a
sidecar-discoverable ConfigMap for an existing Grafana installation.
The [`agentizr`](../agentizr/README.md) application chart bundles it as a `file://` subchart enabled by
`dashboards.enabled`; the dependency currently selects version `0.1.0`. Set the values below under the application
chart's `dashboards` key.

## Usage

With the application chart, select your existing Prometheus datasource UID:

```yaml
dashboards:
  enabled: true
  dashboardFolder: Agentizr
  datasources:
    prometheus: example-prometheus
```

For standalone installation from this checkout:

```powershell
helm upgrade --install example-runtime-dashboards charts\agentizr-dashboards --namespace example-monitoring --set datasources.prometheus=example-prometheus
```

Install this dashboard in only one release per Grafana instance: its UID and ConfigMap name are
stable, not release-qualified. Grafana needs a sidecar watching `grafana_dashboard: "1"` and the
`grafana_folder` annotation. The chart installs neither Grafana nor Prometheus.

## Configuration

| Value | Default | Notes |
| --- | --- | --- |
| `enabled` | `true` | Set `false` to render no dashboard ConfigMaps |
| `dashboardFolder` | `Agentizr` | Grafana sidecar folder annotation |
| `datasources.prometheus` | `prometheus` | Prometheus datasource UID |

Each JSON file under `dashboards/` becomes a ConfigMap labelled `grafana_dashboard: "1"`.
Datasource substitution is restricted to the chart's Prometheus placeholder so Grafana legend
tokens remain unchanged.

## Dashboard

| File | Title | Scope |
| --- | --- | --- |
| `agentizr-runtime.json` | Agentizr | Execution, session ownership/contention, state caching, durable writes and conversation archival |

## Metric Contract

The dashboard consumes these OpenTelemetry instruments after Prometheus translation:

| Prometheus metric | Dimensions |
| --- | --- |
| `agentizr_run_total` | `tenant`, `agent`, `provider`, `model`, `outcome` (`succeeded`, `completed_with_tool_errors`, `failed`, `cancelled`, `conflict`, `unavailable`) |
| `agentizr_run_duration_milliseconds_bucket` | Run dimensions plus `le` |
| `agentizr_run_time_to_first_token_milliseconds_bucket` | Run dimensions plus `le` |
| `agentizr_tokens_total` | Run dimensions plus `token_kind` |
| `agentizr_tool_calls_total` | Run dimensions plus `tool_outcome` (`succeeded`, `error`); includes delegated agents' calls |
| `agentizr_ha_operations_total` | `tenant`, bounded `operation` and `outcome`; see [HA observability](../../docs/high-availability.md#observability) |
| `agentizr_ha_duration_milliseconds_bucket` | HA operation dimensions plus `le`; lock acquisition and durable state write/delete duration |
| `agentizr_ha_session_owners` | `tenant`; active advisory-lock owners, summed across replicas |

Provider and model labels are absent when a requested definition is unavailable. Labels contain
configuration names only; session identifiers, prompts, outputs, tool arguments, and caller IDs are
never exported.

HA panels intentionally use only the tenant filter: persistence instruments have no agent,
provider or model dimensions. Missing data means no matching operation has been observed, not
proof of health. The dashboard UID and ConfigMap identity remain unchanged when moving the folder
to `Agentizr`; Grafana may retain an empty old folder.

Configure the runtime's OTLP exporter and a collector/Prometheus pipeline to make these metrics
available. The dashboard does not scrape the HTTP service, and changing the runtime's
`AppConfig:MetricNamePrefix` from `agentizr` requires corresponding dashboard query changes.

## Documentation

- [Application chart](../agentizr/README.md)
- [Runtime observability](../../README.md#observability)
- [License](../../LICENSE)
