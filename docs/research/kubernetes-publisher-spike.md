# Spike: Aspire's Kubernetes publisher against the AppHost (#924)

Throwaway. The code is [src/Norvia.AppHost.KubeSpike](../../src/Norvia.AppHost.KubeSpike/) on branch `924-spike-aspires-kubernetes-publisher-against-the-apphost`. It is not in `Norvia.slnx` and is not merged. Part of #923.

## Answer

**Write the Helm chart by hand.** Use the publisher's output once, as a checklist of environment variable names, and not as the deployment.

Apply it with **`helm upgrade --install`** from the deploy script that also runs the smoke script. Flux or Argo CD can take the same chart later as a `HelmRelease` or an `Application`, without changing it. `kubectl apply` of rendered manifests is the weakest of the three (see "How manifests are applied").

## What was run

- Aspire 13.6.5, CLI 17.0.0, .NET 10.0.402. `Aspire.Hosting.Kubernetes` exists only as a **preview** (`13.6.5-preview.1.26559.7`); there is no stable 13.6 package.
- **Run 1, the real AppHost plus `AddKubernetesEnvironment("k8s")`**, run through `aspire publish`. It fails: `Compute resource(s) 'tms-migrations', 'tms-api', ... are not assigned to a compute environment, but the model contains multiple compute environments ('k8s', 'apps')`. Sixteen resources need `WithComputeEnvironment`.
- **Run 2, a compact AppHost of the local topology** ([AppHost.cs](../../src/Norvia.AppHost.KubeSpike/AppHost.cs)) with SQL Server, Azurite and two Service Bus emulators as plain containers, five migrations, 11 applications and all 13 `Local*Worker` hosts (#923 says 14. The AppHost has 13 workers, plus `LocalPartnerApi`, which the spike includes, and `LocalAgentMcp`, which it leaves out because `Agents:Enabled` is off by default). `aspire publish` succeeds and writes **one Helm chart: 114 files, 2,733 lines of templates**, 32 ConfigMaps, 29 Secrets, 17 Services, 33 Deployments and 1 StatefulSet. `helm` and `kubectl` are not installed here, so the chart was **not linted, rendered or applied** and nothing ran on K3S.
- **Run 3, run 2 plus one `AddAzureStorage(...).RunAsEmulator()` resource**, to see what an Azure resource does in a Kubernetes-only model.

## What the publisher generates

Good, and free:

| | |
|---|---|
| Workers | All 13 become a Deployment, a ConfigMap and a Secret each. Image names are chart values (`tms_api_image: "tms-api:latest"`). |
| Wiring | Service names and ports resolve in the templates (`TmsApi__BaseAddress: http://tms-api-service:{{ .Values.parameters.tms_api.port_http }}`), and so do connection strings for SQL Server, Azurite and the emulators. |
| Secrets | Parameters marked `secret: true` (`local-token-signing-key`, `driver-app-endpoint-key`, the SQL password) become `Secret`s fed by `secrets.*` values, empty in `values.yaml`. |
| Dashboard | A standalone Aspire dashboard (`k8s-dashboard`) is added and every app gets `OTEL_EXPORTER_OTLP_ENDPOINT` for it. That is the "standalone dashboard first" decision of #923, for nothing. |

Missing or wrong for #923, each seen in the output:

| Need | What the publisher does |
|---|---|
| **Migrations run once** | Each `*-migrations` project becomes a `Deployment` (`replicas: 1`), so the pod restarts forever. There is no `Job`, and no Helm hook. |
| **Start order** (`WaitFor`, `WaitForCompletion`) | Dropped. No `initContainers`, no probes. |
| **Health checks** (`WithHttpHealthCheck("/health")`) | Dropped. No `livenessProbe` or `readinessProbe` anywhere. |
| **SQL Server data on a PVC** | `WithDataVolume()` becomes `emptyDir: {}`. The five databases vanish with the pod. Same for Azurite. No `PersistentVolumeClaim` is generated. |
| **The Service Bus emulator's `Config.json`** | `WithBindMount` fails the publish (`Bind mounts are not supported by the Kubernetes publisher`). `WithContainerFiles` is **silently dropped**: the deployment has no volume, and nothing in the chart mentions `Config.json`. The spike's own `sb-config.json` is an empty stub, so it shows only that the mount is missing from the chart, not what the real `Config.json` (written by `ResubmitAwareFilters`) needs. |
| **Namespace, default-deny `NetworkPolicy`, `ResourceQuota`, resource limits, ingress** (#938, #923 "Edge") | None generated. `WithExternalHttpEndpoints()` still makes a `ClusterIP` Service. Only kinds present: ConfigMap, Deployment, Secret, Service, StatefulSet. |
| **Values the AppHost computes at publish time** | They are written into `values.yaml` as literals. `Seed__Path` is `/home/<user>/Work/.../seed/norvia.seed.json`, a developer's path. The per-run random Carrier keys (`RandomNumberGenerator` in the AppHost) are fixed at publish time and the same key sits in the worker's and the portal's ConfigMap, a non-secret. |
| **Shared file** | `DriverReports__DisabledFlagPath` is `/tmp/norvia-driver-reports-disabled-<guid>`. The Partner portal creates the file and `LocalDriverReportsWorker` reads it, so under Aspire they share a disk. In two pods they do not. This needs a design decision (a shared volume, or the flag moves to the TMS or a table), whichever way the chart is built. |
| **Azure resources** (run 3) | `AddAzureStorage` writes `storage/storage.bicep` and `main.bicep` next to the chart, warns `No azure compute environments found`, and leaves `ConnectionStrings__azblobs: ""` in `values.yaml`. `RunAsEmulator`, `RunAsContainer` and `WithRoleAssignments` do nothing in publish mode. |
| **Name clash** | A connection string and a container cannot share a name: the emulator container `messaging` collided with `AddConnectionString("messaging")`, so the containers had to be renamed `sb-messaging`, `sb-messaging-2`. |
| **Size** | The generated sql password is repeated under `secrets.*` for each of 16 consumers (`sql_password`, `ConnectionStrings__tms`, `TMS_URI`, ...). Each database reference also adds `*_URI`, `*_JDBCCONNECTIONSTRING`, `*_HOST` and `*_PORT` variables next to the connection string, whether or not the application reads them. |

Not tried: `PublishAsKubernetesService(...)`, the callback that edits a generated resource. It might fix some of the table (probes, Job kind), but it would put Kubernetes YAML structure into C# and still not reach the namespace, policy and quota pieces.

## The three options

| | A. Second AppHost | B. Third mode in the current AppHost | C. Hand-written Helm chart |
|---|---|---|---|
| **Fits the sandbox** | A local-topology AppHost that shares the project references. Run 2 is a sketch of it at 130 lines, but the real wiring is the 1,224-line `AppHost.cs`: personas, keys, 13 workers' settings, filters. A second file copies it. | One file. But it has 5 `IsPublishMode` sites, 31 lines with `PublishAsAzureContainerApp`, `PublishAsExisting` or `WithRoleAssignments` and 106 `estate.` references, and `IsPublishMode` is binary. A third mode means a switch outside `IsPublishMode`, and `AddSqlServer`, containers and emulator config in place of the Azure types in each branch. | Nothing in the AppHost changes. The chart is a second place that knows each application's environment variables. |
| **What it generates** | Run 2's output, with the gaps above. | Run 1: it does not publish until every resource names its compute environment, and any Azure resource left in the model writes Bicep and an empty value (run 3). | Whatever is written: `Job` or hook for migrations, probes, PVCs, `ConfigMap` for the emulator config and the seed, `Namespace`, `NetworkPolicy`, `ResourceQuota`, `Ingress`, limits. All are in #923 and #938 as chart values. |
| **Gaps closed how** | Post-process the chart (kustomize patches or a script) or `PublishAsKubernetesService`. Still a second source to maintain, and the output is `0.1.0` preview-shaped. | Same, plus the risk to the shared file: the integration and UI test projects use `Aspire.Hosting.Testing` against the AppHost, and `azd` publishes it to Azure. | Directly. |
| **Drift** | Two AppHosts: wiring drifts between them. | None between modes, but each Azure/local branch drifts from the others inside one file. | The chart can drift from the AppHost's environment variables. See the check below. |
| **Tests** (#923: template tests) | Snapshot tests of generated YAML, which change with every Aspire preview. | Same, and an AppHost test per mode. | `helm template` plus `helm lint`, and plain template tests on the values the issue lists. |

### Why C

1. The publisher does not produce the pieces #923 asks for (namespace policy, ingress, quota, PVCs, migrations as Jobs, probes, the emulator config), and A and B both have to add them after the fact. In C they are the work itself.
2. The package is a preview, its generated chart drops three things silently (the emulator config, health checks, start order), and a spike on one preview version is not evidence that the next one keeps its output stable.
3. B puts Kubernetes, Azure and run mode into the one file that `azd` and the integration tests depend on. A copies it.
4. The cost of C is real: environment variables are written twice. Keep that cost small with a check, not a convention: a test starts the AppHost's model in run mode (`Aspire.Hosting.Testing`, which the repo already uses), takes each project's environment variable names, and compares them with those in `helm template` output for the same project, ignoring the names the chart replaces on purpose (connection strings, `LocalAuth:Allowed`). This is a proposal and was not built here.

### One use of the publisher

Run 2's `values.yaml` is a complete list of the settings the 13 workers, the migrations and the applications take, with the secrets named. Start the chart from it. Once.

## How manifests are applied

| | `kubectl apply` of rendered manifests | `helm upgrade --install` | Flux or Argo CD |
|---|---|---|---|
| Migrations before the apps | No ordering. A `Job` must finish before the Deployments, and `kubectl` does not wait. | A `pre-upgrade` hook `Job`, or ordering in the script. | Hooks work in Argo CD; Flux's `HelmRelease` runs Helm hooks. |
| Pruning what was removed | No. | Yes, per release. | Yes. |
| Secrets in values (`secrets.*`) | Rendered into files. | `--set` or a values file from the operator. | Needs SOPS or an external secret store. Not needed in the sandbox. |
| Fits "one smoke script after every deploy" (#923) | Yes. | Yes: deploy, then smoke. | Smoke runs after a sync, which is a second moving part. |
| Needs on the cluster | Nothing. | Nothing. | A controller, in a cluster shared with other applications (#923 "Already decided"). |

`aspire deploy` for a Kubernetes environment calls Helm itself (`HelmDeploymentEngine`, `HelmVersionValidator` appear in the stack trace of a `--publisher default` run), so Helm is also the publisher's native apply path.

**Recommendation:** `helm upgrade --install` in a script. Keep the chart a plain chart so Flux or Argo CD can adopt it with no change, and decide that when the cluster owner has a GitOps controller. `kubectl apply` of rendered YAML is not recommended.

## For the ADR

- **Decision:** the Kubernetes deployment is a hand-written Helm chart in the repo. The AppHost stays as it is (Azure Container Apps in publish mode, Aspire locally). Not a second AppHost, not a third mode.
- **Supersedes nothing, to be confirmed when the ADR is written** (not checked against ADR 0004's full text). [ADR 0004](../adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md) is about Azure; this adds a third place.
- **Consequences:** the chart and the AppHost both know the applications' settings (the check above keeps them together); the Driver reports "disabled" flag file needs a design for two pods; the emulators' `Config.json` and the seed file go in a `ConfigMap` or an image; no Dapr, no Azure at runtime, as in #923.
- **Not verified here:** `helm lint` and `helm template` on any chart, anything on K3S, `PublishAsKubernetesService`, a stable Aspire Kubernetes package.

## Reproduce

```
dotnet build src/Norvia.AppHost.KubeSpike
cd src/Norvia.AppHost.KubeSpike && aspire publish --non-interactive --no-build -o <dir>
```

Do not use `--publisher default` or `--operation publish` without `aspire`: the first runs Helm uninstall and fails without Helm, and the second starts the AppHost as a normal run.
