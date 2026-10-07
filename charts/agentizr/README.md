# agentizr

Umbrella chart for the CasCap Agent Runtime HTTP service and its external EF Core migration job.

## Overview

The chart aliases the shared `workload` chart twice:

| Alias | Purpose |
| --- | --- |
| `runtime` | Long-running Agent Runtime HTTP service |
| `migrate` | One-shot `agentizr --migrate` job for externally applied migrations |

Both aliases default to zero replicas. A deployment supplies PostgreSQL, Redis, JWT tenancy,
definition publication policy and credential configuration through environment variables and
Secrets, then enables both workloads. Configure the migration Job as an Argo CD PreSync hook so it
completes before the runtime Deployment rolls.

## Usage

```yaml
runtime:
  replicaCount: 1
  image:
    repository: ghcr.io/example/agent-runtime
    tag: example

migrate:
  replicaCount: 1
  image:
    repository: ghcr.io/example/agent-runtime
    tag: example
  job:
    annotations:
      argocd.argoproj.io/hook: PreSync
      argocd.argoproj.io/hook-delete-policy: HookSucceeded
```

Never place connection strings, authorization headers, API keys or MCP credential parameters in
chart values. Inject them from Kubernetes Secrets.

## Dependencies

| Chart | Version | Alias |
| --- | --- | --- |
| `oci://ghcr.io/f2calv/charts/workload` | `1.1.0` | `runtime` |
| `oci://ghcr.io/f2calv/charts/workload` | `1.1.0` | `migrate` |
