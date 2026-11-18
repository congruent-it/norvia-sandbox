# The Norvia chart

Runs the sandbox's local topology on Kubernetes ([ADR 0053](../../../docs/adr/0053-kubernetes-runs-the-local-topology.md)). Applied with `helm upgrade --install`. Template tests: `pwsh -c 'Invoke-Pester infra/chart-data-tier.Tests.ps1, infra/chart-service-bus.Tests.ps1, infra/chart-edge.Tests.ps1'`.

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

`localTokens`, `personas` and `agentMcpServers` in `values.yaml` repeat what `LocalTokens.cs`, the seed file and `AppHost.cs` hold; the template tests compare them. The other UIs and APIs (the TMS, the ERP, the console and the rest) are not in the chart yet: each joins the `exposed` list of `templates/ingress.yaml` when it is deployed, and the Services the MCP stand-in and the Partner API name (`tms-api`, `erp-api`, `integration-console-web`, `health-check-web`, `carrier-invoice-worker`) are those the AppHost's resources are called. Not verified on a cluster: Traefik and cert-manager, which the smoke script (#932) covers.
