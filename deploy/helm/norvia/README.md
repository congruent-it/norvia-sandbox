# The Norvia chart

Runs the sandbox's local topology on Kubernetes ([ADR 0053](../../../docs/adr/0053-kubernetes-runs-the-local-topology.md)). Applied with `helm upgrade --install`. Template tests: `pwsh -c 'Invoke-Pester infra/chart-data-tier.Tests.ps1, infra/chart-service-bus.Tests.ps1'`.

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
