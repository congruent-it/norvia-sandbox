# The Norvia chart

Runs the sandbox's local topology on Kubernetes ([ADR 0053](../../../docs/adr/0053-kubernetes-runs-the-local-topology.md)). Applied with `helm upgrade --install`. Template tests: `pwsh -c 'Invoke-Pester infra/chart-data-tier.Tests.ps1, infra/chart-service-bus.Tests.ps1, infra/chart-edge.Tests.ps1, infra/chart-workers.Tests.ps1, infra/chart-telemetry.Tests.ps1, infra/chart-namespace.Tests.ps1'`.

## The data tier (#926)

| Piece | What it is |
|---|---|
| `sql` | One StatefulSet holding `tms`, `erp`, `driver-app`, `partner-portal` and `simulator`, on a PVC of `persistence.storageClass` (empty: the cluster default). No backup, no HA: a lost volume is rebuilt from the seed file. The SA password is generated at install, kept on upgrade, or set with `sql.saPassword`. |
| `azurite` | The blob service only, on its own PVC. The apps create their containers themselves. `include "norvia.azuriteConnectionString" .` gives the string for `ConnectionStrings__blobs` (the Driver app: `ConnectionStrings__driver-app-blobs`). |
| `<name>-migrations-<revision>` | One Job per migrations project. It creates its database if missing, then migrates. Named with the release revision because a Job is immutable; Helm prunes the old ones. Not a Helm hook, so `helm --wait` cannot deadlock on applications waiting for it. |
| `migrations-waiter` | A service account that may only read Jobs. |

An application waits for its migrations by running as `migrations-waiter` and including the helper:

```yaml
serviceAccountName: migrations-waiter
initContainers:
  {{- include "norvia.waitForMigrations" (dict "root" . "names" (list "tms")) | nindent 2 }}
```

## The Service Bus emulators (#927)

| Piece | What it is |
|---|---|
| `messaging`, `messaging-2` | Two emulators, each a Deployment, a Service (AMQP on 5672, management on 5300) and a ConfigMap holding its `Config.json`, as under Aspire: each accepts only 10 concurrent connections. `messaging-2` holds the Driver app's, the ERP's and the Carrier tracking path's topics and queues. Their state is in the `sql` StatefulSet, so each waits for it. |
| `serviceBus.emulators` in `values.yaml` | The topics, subscriptions and queues. `serviceBus.conditions` holds each filtered subscription's condition; the template adds the resubmit clause and brackets a condition with an `OR`. The template tests compare every expression with `infra/estate/messaging.bicep`. |

An application asks for either emulator as `ConnectionStrings__messaging`; the Integration console reads the entities through the management port:

```yaml
- name: ConnectionStrings__messaging
  value: {{ include "norvia.serviceBusConnectionString" (dict "root" . "name" "messaging-2") | quote }}
- name: LocalServiceBus__Namespaces__messaging-edge__ManagementConnectionString
  value: {{ include "norvia.serviceBusManagementConnectionString" (dict "root" . "name" "messaging-2") | quote }}
```

Lock durations are the Bicep's (the AppHost's emulators set none). Not verified on a cluster: that two emulators can share one SQL Server (the spike's chart did the same; Aspire gives each its own). The smoke script is the check.

## The edge (#928)

| Piece | What it is |
|---|---|
| `partner-api` | `LocalPartnerApi`, which plays API Management and the intake and Carrier-answer Logic Apps (ADR 0023): a Deployment, one replica (its rate limits are in memory), and a Service on `appPort` (8080), probed on `/health` and `/alive`. It reads the `messaging-2` emulator, Azurite and the TMS API, with the settings the AppHost gives it. |
| `agent-mcp` | `LocalAgentMcp`, the MCP servers of the Agents (ADR 0041). Only when `agents.enabled` is true, as under Aspire; the same flag turns on the Partner API's Intake agent. It calls the agent views of `integration-console-web`, `health-check-web`, `tms-api` and `erp-api` by their Service names. |
| `local-tokens`, `subscription-keys` | Generated at install and kept on upgrade (a lookup, as for the SA password; `helm template` renders new ones). `local-tokens/signing-key` is the key every local stand-in signs and checks tokens with, base64 as JWT bearer configuration takes it. `subscription-keys` holds a key per Persona and one for the Consignee tracking product, under the names of the settings that read them (`PartnerApi__SubscriptionKeys__<persona>`), so the Partner API, the Partner portal and the Simulator all take it as `envFrom`. No key is in a values file. |
| `<application>` Ingress | One per application, at `<host>.<edge.baseDomain>` on `edge.ingress.className` (Traefik on K3S is the tested default). With `edge.tls.clusterIssuer` set, the Ingress asks cert-manager for a certificate into the Secret `<application>-tls`; with no issuer it expects that Secret to exist; `edge.tls.enabled=false` leaves TLS out. The chart installs no controller and no issuer. |

`edge.baseDomain` has no default, and the chart does not render without it. DNS is the operator's: a wildcard record for the base domain, pointing at the ingress controller. The registry is `image.registry` and `image.tag`.

```
helm upgrade --install norvia deploy/helm/norvia -n norvia \
  --set edge.baseDomain=norvia.example.org --set edge.tls.clusterIssuer=letsencrypt \
  --set image.registry=registry.example.org/norvia
```

`localTokens`, `personas` and `agentMcpServers` in `values.yaml` repeat what `LocalTokens.cs`, the seed file and `AppHost.cs` hold; the template tests compare them. The other UIs and APIs (the TMS, the ERP, the console and the rest) are not in the chart yet: each joins the `exposed` list of `templates/ingress.yaml` when it is deployed, and the Services the MCP stand-in and the Partner API name (`tms-api`, `erp-api`, `integration-console-web`, `health-check-web`, `carrier-invoice-worker`) are those the AppHost's resources are called. Not verified on a cluster: Traefik and cert-manager, which the smoke script (below) covers.

## The Logic App workers (#929)

| Piece | What it is |
|---|---|
| `workers` in `values.yaml` | The 13 `Local*Worker` hosts, each a Deployment of one replica (`templates/workers.yaml`): its settings, the Service Bus emulator it reads (`messaging` or `messaging-2`, as the AppHost's `messaging` and `messagingEdge`), the application it calls (`tms` or `erp`), the client ID of its token identity and, for the five that call a Shipper's or Carrier's endpoint, the Persona endpoints. They are plain generic hosts, so they have no port and no probes; `carrier-invoice-worker` is the exception (the Partner API calls its HTTP trigger), with a Service and the `/health` and `/alive` probes. `workerResources` holds their requests and limits. |
| `endpoint-keys` | Generated at install and kept on upgrade, like `subscription-keys`: the credential each Shipper's and Carrier's endpoint in the Partner portal asks its caller for, and the Driver app's Trip endpoint asks the Trip dispatch worker for. The keys are named as the settings that read them (`CarrierEndpoint__ApiKeys__CAR-0001`, `ShipperEndpoint__ApiKeys__SHP-0001`, `DriverEndpoint__ApiKey`), so the Partner portal and the Driver app take the Secret as `envFrom` and a worker reads its key with `secretKeyRef`. |

Every worker signs its tokens with `local-tokens/signing-key`. The workers call `tms-api`, `erp-api`, `driver-app-api` and `partner-portal-web` by the AppHost's resource names; those applications are not in the chart yet, so until they are a worker starts and retries. Not in this slice: the Driver reports worker's disabled flag (`DriverReports__DisabledFlagPath`), a file the Partner portal's fault page creates in the AppHost's temp directory. It is unset here, so the worker never reads as disabled; it needs a volume shared with the Partner portal, which comes with that application.

## Telemetry phase 1: the standalone Aspire dashboard (#931)

| Piece | What it is |
|---|---|
| `aspire-dashboard` | The Aspire dashboard (`telemetry.dashboard.image`, the major.minor of the Aspire packages) as a Deployment of one replica, with a Service on 18888 (UI and telemetry API), 18889 (OTLP over gRPC) and 18890 (OTLP over HTTP). No volume: it keeps its telemetry in memory, up to `telemetry.dashboard.maxLogCount` log records, so history does not survive a restart (retention is a separate issue). Only the log count is capped: a burst of traces or metrics can OOM-kill the pod at its memory limit and drop the history. The telemetry API key is kept through `lookup`; rendering without cluster access (`helm template`, client-side GitOps) generates a new key, which restarts the dashboard (checksum annotation), so restart the Integration console with it. OTLP takes no key and is reachable only inside the cluster. The UI has no Ingress and keeps the dashboard's browser token (in the pod's log): `kubectl port-forward svc/aspire-dashboard 18888`. |
| `dashboard-api-key` | The key the telemetry API asks for in the `x-api-key` header, generated at install and kept on upgrade, like the other keys. |
| OTLP settings | `norvia.deployment` gives every application `OTEL_EXPORTER_OTLP_ENDPOINT` (`http://aspire-dashboard:18889`, which `ServiceDefaults` exports to over gRPC) and `OTEL_SERVICE_NAME`, so an application the chart deploys later sends its telemetry without asking. |
| `norvia.localTelemetryEnv` | `LocalTelemetry__DashboardUrl` and `LocalTelemetry__ApiKey`, which make the Integration console search the dashboard with `DashboardTrailSource` (ADR 0027). The console is not in the chart yet: its Deployment includes this helper when it is added. |

Not verified on a cluster: that the dashboard takes its settings as environment variables of this spelling, and that a search finds a trail written by a running application. The smoke script (#932) does not cover either yet: a search for the run's trail waits for the Integration console, which is not in the chart yet.

## The namespace: network, ingress, Secrets and quota (#938)

| Piece | What it is |
|---|---|
| `default-deny`, `allow-*` | `templates/network-policies.yaml`. A policy that selects every pod and allows nothing, then one policy per path: `allow-same-namespace` (pods of the namespace to each other; the pairs are not enumerated, as the applications are still being added), `allow-dns`, `allow-ingress-controller` (the controller's namespace, `networkPolicy.ingressController.namespace`, to the applications of `norvia.exposedApplications`, on `appPort`) and `allow-api-server` (egress on 443 and 6443 to `networkPolicy.apiServer.cidrs`, for pods labelled `norvia.io/waits-for-migrations: "true"`: a pod that includes `norvia.waitForMigrations` must carry that label, as well as run as the service account `migrations-waiter`, or its init container cannot reach the API server). |
| `internal-only` | A Traefik `Middleware` (`edge.internal`) that every Ingress names; with `nginx` an annotation instead, with `none` the class or annotations you give. See `values.yaml` for the caveat about the client's address. |
| `norvia`, `secret-reader` | The service account every pod runs as (no token mounted; `norvia.podAccount` sets it, `migrations-waiter` aside), and a Role and RoleBinding that let it and `rbac.secretReaders` `get` the chart's five Secrets by name. The template test compares the Role's names with the Secrets the chart renders. |
| `norvia` ResourceQuota, `defaults` LimitRange | `templates/quota.yaml`, from `quota` in `values.yaml`. Every Deployment, StatefulSet and Job has requests and a memory limit in `values.yaml`; the LimitRange covers the init containers. The template test sums what the chart asks for, the Jobs included, and fails when it exceeds the quota. |
| `Namespace` | Only with `namespace.create=true`; see `values.yaml` for why Helm's own `--create-namespace` is the default. |

The chart installs nothing cluster-scoped (a template test lists the kinds). Not verified on a cluster: that the CNI enforces the policies, which the smoke script's isolation check settles, and the Pod Security `baseline` label.

## The smoke script (#932)

`infra/smoke-test-kubernetes.ps1` (PowerShell 7, ADR 0048) is the check after every `helm upgrade --install`. Run it from anywhere that reaches the ingress, with `kubectl` pointed at the cluster:

```
pwsh infra/smoke-test-kubernetes.ps1 -BaseDomain norvia.example.org   # -Insecure for plain http, -SkipCertificateCheck for a self-signed certificate
```

It drives one Shipment through the ingress hostnames: Brouwer Dranken books, the Planner assigns and releases (as the TMS UI would), Van Dijk Transport confirms, sends PU, DEP and POD tracking events and a Proof of delivery, and Brouwer Dranken reads the Proof of delivery, its file and the tracking events back. It signs the tokens itself with `local-tokens/signing-key` and takes the keys from `subscription-keys`, so it needs `get` on those Secrets. It stops at the first step that fails, naming it, with the status the application answered and never a body, token or key. Each run leaves one Transport Order behind.

Before the Shipment, it runs the isolation check of #938: a pod in a namespace it makes and removes must be refused by the Partner API, SQL Server and the two emulators (`-SkipIsolationCheck` leaves it out, `-ProbeImage` sets the image).

What it settles for the "not verified on a cluster" notes above: that Traefik and cert-manager route to the Partner API, that two Service Bus emulators share one SQL Server, and the worker path (booking, tracking and Proof of delivery) through the emulators and Azurite. It needs the TMS API on `tms-api.<baseDomain>`, and until that application is in the chart the script fails at its first step, naming it. Not covered yet: that the Aspire dashboard's search finds the run's trail, which waits for the Integration console.
