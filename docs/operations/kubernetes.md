# Kubernetes

How to run the sandbox's local topology on a Kubernetes cluster, set the chart's values, decide whether `LocalAuth:Allowed` is safe on your cluster, and check a deployment with the smoke script. What plays each Azure piece there is the **On Kubernetes** column of [architecture/local-vs-azure.md](../architecture/local-vs-azure.md); why it is built this way is [ADR 0053](../adr/0053-kubernetes-runs-the-local-topology.md). What each piece of the chart is, is in [deploy/helm/norvia/README.md](../../deploy/helm/norvia/README.md).

The chart runs the **local topology**, not the Azure estate: no Azure account, Entra, Key Vault, Foundry or Application Insights, and the as-found toggles, the alerts and the real Estate health check stay Azure-only. Nodes must be amd64, because SQL Server and the Service Bus emulator have no ARM images.

## What is in the chart

![Kubernetes: the local topology as pods in the norvia namespace](../architecture/diagrams/kubernetes.png)

The editable source is [kubernetes.excalidraw](../architecture/diagrams/kubernetes.excalidraw), generated from [deployment.cs](../architecture/diagrams/deployment.cs).

In the chart: SQL Server and Azurite on PVCs, the migrations as Jobs, the two Service Bus emulators, the Partner API stand-in (`LocalPartnerApi`) and the agent MCP stand-in (when `agents.enabled`), the TMS, ERP and Driver app APIs and UIs, the Integration console and the Estate health check, the Partner portal and the Simulator, the 13 `Local*Worker` Deployments, the standalone Aspire dashboard, an Ingress per exposed application, and the namespace's protection ([below](#the-namespace-is-closed-to-the-others)): a default-deny `NetworkPolicy`, an internal-only ingress, Secrets read by name, requests and limits on every pod, and a `ResourceQuota` with a `LimitRange`.

Every application of the AppHost is in the chart: `tms-api`, `tms-web`, `erp-api`, `erp-web`, `driver-app-api`, `driver-app-web`, `integration-console-web`, `health-check-web`, `partner-portal-web` and `simulator-web`, each reached at `<name>.<baseDomain>`. They get the environment variables the AppHost gives them, under the same names, and a template test compares the two.

## Before you deploy

- **A cluster** with an ingress controller (Traefik on K3S is the tested default), a default storage class or one you name, and, if you want certificates, cert-manager with a `ClusterIssuer`. The chart installs none of these.
- **DNS:** a wildcard record for the base domain, pointing at the ingress controller. Each application is reached at `<host>.<baseDomain>`.
- **The images**, built and pushed to a registry the cluster can pull from (`image.registry`, `image.tag`). Forgejo builds them and tags them with the short commit SHA ([forgejo.md](forgejo.md#the-images)).
- **Tools:** `helm`, `kubectl` and PowerShell 7.4 or later. The [dev container](dev-container.md) has them.

## Deploy

```bash
helm upgrade --install norvia deploy/helm/norvia -n norvia --create-namespace \
  --set edge.baseDomain=norvia.example.org \
  --set edge.tls.clusterIssuer=letsencrypt \
  --set image.registry=registry.example.org/norvia
```

Use `helm upgrade --install`, not `kubectl apply` of rendered manifests: only Helm orders the migrations before the applications and prunes what was removed. The chart is plain, so Flux or Argo CD can take it later as a `HelmRelease` or an `Application`. Note that rendering without cluster access (`helm template`, client-side GitOps) cannot look up the generated Secrets, so it renders new keys on every run.

The generated Secrets (`local-tokens`, `subscription-keys`, `endpoint-keys`, the SQL Server SA password and the dashboard's API key) are made at install and kept on upgrade. No key is in a values file unless you set `sql.saPassword` yourself; keep that out of source control.

## The values you set

| Value | What it does |
|---|---|
| `edge.baseDomain` | **Required, no default.** The hostnames are `<host>.<baseDomain>`. |
| `edge.ingress.className` | The ingress class. `traefik`. |
| `edge.tls.clusterIssuer` | The cert-manager issuer that signs each hostname's certificate. Empty: the Ingress asks for none and expects a Secret `<application>-tls`. `edge.tls.enabled=false` leaves TLS out. |
| `image.registry`, `image.tag`, `image.pullSecrets` | Where the Norvia images come from. Empty registry: the image names are used as they are, which suits a cluster that has them loaded. |
| `persistence.storageClass` | The storage class of every PVC. Empty: the cluster's default. |
| `sql.saPassword` | Empty: generated. Must not contain a semicolon. |
| `localAuth.allowed` | `LocalAuth:Allowed` for the applications that have a local sign-in or trust the local key (`tms-api`, `tms-web`, `erp-api`, `erp-web`, `driver-app-api`, `driver-app-web`, `simulator-web`). `true` by default; read [the section below](#localauthallowed) first. |
| `agents.enabled` | Off, as under Aspire. On, the Partner API runs the Intake agent's scripted fake and `agent-mcp` is deployed. No model is called either way. |

The rest of `values.yaml` (the emulators' topics, the Personas, the token constants, the workers) repeats what the AppHost and the seed file hold, and the chart's template tests compare them: change one, change the other.

## The namespace is closed to the others

Everything Norvia deploys is in one namespace, on a cluster other applications share (#938). The chart protects both sides:

- **Namespace.** Helm cannot make the namespace it stores its release in, so `--create-namespace` makes it. Label it once: `kubectl label namespace norvia app.kubernetes.io/part-of=norvia pod-security.kubernetes.io/enforce=baseline pod-security.kubernetes.io/warn=restricted`. A GitOps tool that applies what the chart renders can set `namespace.create=true` and `namespace.labels` instead. The chart puts nothing outside the namespace and installs nothing cluster-scoped: no ingress controller, issuer, storage class, CRD or cluster role.
- **Network.** A `default-deny` `NetworkPolicy` selects every pod, in and out. The paths opened are: pods of the namespace to each other; DNS; the ingress controller to the applications that have an Ingress, on 8080; and the pods that wait for the migrations to the API server. No other namespace reaches the Partner API, SQL Server, the Service Bus emulators or a worker. Set `networkPolicy.ingressController.namespace` if your controller is not in `kube-system`, and narrow `networkPolicy.apiServer.cidrs` (on K3S, the node's address). **A CNI that does not enforce `NetworkPolicy` makes all of this a no-op**; the smoke script's isolation check is how you find out.
- **Internal-only ingress.** Every Ingress is restricted to the LAN or VPN: with `edge.internal.controller=traefik` (the default) the chart renders a Traefik `Middleware` admitting `edge.internal.allowedCidrs` (the private ranges), with `nginx` it sets the `whitelist-source-range` annotation, and with `none` you give an internal ingress class (`edge.internal.className`) or your controller's annotations (`edge.internal.annotations`) and the chart does not render without one. An allow list sees the client's address only if the controller does (`externalTrafficPolicy: Local` on its Service, or the proxy protocol); behind a load balancer that rewrites it, use an internal ingress class.
- **Secrets.** The pods run as the service account `norvia`, which has no API token mounted. A `Role` lets that account and the subjects in `rbac.secretReaders` `get` the chart's Secrets by name, with no `list` or `watch`. RBAC only adds, so the chart grants nothing else on Secrets; what other roles of the cluster can read is the cluster's. The operator who runs the smoke script needs to be a subject, or to hold their own rights.
- **Requests and limits.** Every Deployment, StatefulSet and Job has CPU and memory requests and a memory limit (no CPU limits). SQL Server holds its own memory under its limit (`sql.memoryLimitMb`, as `MSSQL_MEMORY_LIMIT_MB` is under Aspire). Init containers have their own (`initContainerResources`), a `LimitRange` gives any container that names none small defaults, and a `ResourceQuota` (`quota.hard`) caps the namespace's pods, requested CPU and memory, limited memory and volumes, sized for the chart and about ten more applications. A template test fails when the chart outgrows the quota. `quota.enabled=false` leaves both out on a cluster that sets its own.

Not verified on a cluster: that a given CNI enforces the policies (the smoke script's isolation check), that Traefik admits only the allowed ranges, and the Pod Security `baseline` label against the images.

## `LocalAuth:Allowed`

There is no identity provider on the cluster. The local operator sign-in ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)), the Driver app's sign-in page ([ADR 0037](../adr/0037-drivers-sign-in-to-the-driver-app-with-entra-and-the-driver-id-is-their-app-role.md)) and the local token-signing key refuse to start outside Development. `LocalAuth:Allowed` is the named setting, independent of the environment name, that lets them run on Kubernetes. It is off by default: set it to `true` (the environment variable is `LocalAuth__Allowed`) and they start; a value that is not `true` or `false` stops the application at startup. While it is on, an Easy Auth header is not trusted, so an application that is given no local sign-in refuses to start. The check is `LocalAuth` in `Norvia.ServiceDefaults`, which the sign-in classes and the TMS, ERP and Driver app APIs (for the local signing key) call.

Setting it is a statement that this holds: **Norvia's ingress is not reachable from the public internet, and no other namespace can reach Norvia's pods.** Anyone who can reach the ingress is signed in as an operator holding both console roles, and anyone who can reach a pod can get a token signed by the local key.

Before you set it, check:

1. The ingress controller that serves Norvia is reachable only from your LAN or VPN, not from the internet.
2. The cluster's CNI enforces `NetworkPolicy`. On one that does not, the namespace's default-deny policy does nothing and the condition is not true. Do not set it there.
3. The namespace has the default-deny policy and the internal-only ingress, which the chart gives it (below), and the smoke script's isolation check passes on your cluster. Do not turn off `edge.internal.enabled`, or leave `edge.internal.controller` as `traefik` or `nginx` behind a load balancer that hides the client's address, without an internal ingress class instead.

## Check a deployment

After every `helm upgrade --install`, from anywhere that reaches the ingress and has `kubectl` pointed at the cluster:

```bash
pwsh infra/smoke-test-kubernetes.ps1 -BaseDomain norvia.example.org
```

Pass `-Insecure` when the ingress is plain http, `-SkipCertificateCheck` for a self-signed certificate, `-Namespace` if it is not `norvia`, and `-TimeoutSeconds` (default 180) for each wait. `-ProbeImage` sets the image of the isolation check's probe pods (`registry.k8s.io/e2e-test-images/agnhost`; use a mirror if the cluster cannot pull from there), `-SkipIsolationCheck` leaves the check out, for an account that may not create a namespace, `-SkipIngressCheck` leaves out the Ingress check (below), and `-Agents` adds `agent-mcp` when the chart has `agents.enabled`. Without `-BaseDomain` it takes `NORVIA_BASE_DOMAIN` from the environment, then the domain of the `partner-api` Ingress.

First it reads every application's Ingress and fails one that is not restricted to the private ranges (the chart's `internal-only` middleware, or the `whitelist-source-range` of ingress-nginx, with no public range in it). It can only read what the controller is told, because the script reaches the ingress from inside those ranges; with `edge.internal.controller=none` the restriction is your internal ingress class, which it cannot read, so pass `-SkipIngressCheck` and check it yourself. Then it checks the isolation: a throwaway pod in a namespace made for the run connects to every application (all 11, 12 with `-Agents`), SQL Server and the two emulators and must be refused (after the same pod, inside the sandbox's namespace, has connected, so a probe that cannot run is not mistaken for a refusal). The namespace is removed after. Then it waits for every application to answer `/health` on its hostname, with none skipped for being missing, and drives one Shipment from booking to delivery through the ingress: Brouwer Dranken books, the Planner assigns and releases, Van Dijk Transport confirms, sends PU, DEP and POD tracking events and a Proof of delivery, and Brouwer Dranken reads the Proof of delivery, its file and the tracking events back. It signs the tokens itself with `local-tokens/signing-key` and takes the keys from `subscription-keys`, so it needs `get` on those Secrets. It stops at the first failing step, naming it, with the status the application answered and never a body, token or key. Each run leaves one Transport Order behind.

A pass settles what the chart's README lists as not verified: that the ingress controller and cert-manager route to every application, that every Ingress is restricted and every Service closed to another namespace, that two emulators share one SQL Server, and that the booking, tracking and Proof of delivery path works through the emulators and Azurite. It does not cover that the Aspire dashboard finds the run's trail, because the smoke script does not search for it. Check it by hand: search the run's correlation ID on the Integration console's trail search (its home page).

## What goes wrong

- **The smoke script fails at the Partner portal.** Look at `kubectl logs deploy/partner-portal-web -n norvia`: it stops at startup when a Persona has no key or client ID. If it fails at the TMS or ERP API, look at `kubectl logs deploy/tms-api -n norvia` (or `erp-api`): an application that finds `LocalAuth:Allowed` off refuses to start.
- **A pod stays in `Init`.** Its init container waits for a migrations Job: `kubectl get jobs -n norvia`, then the Job's log.
- **The dashboard's history is gone.** It keeps telemetry in memory and loses it on a restart. A rendered-without-lookup upgrade also changes its API key; restart the Integration console with it.
- **Data is gone.** A lost SQL Server or Azurite volume is not backed up. Reinstall, and the seed file loads the Shippers, Carriers and Rate Cards again.
- **The chart's template tests:** `pwsh -c 'Invoke-Pester infra/chart-data-tier.Tests.ps1, infra/chart-service-bus.Tests.ps1, infra/chart-edge.Tests.ps1, infra/chart-workers.Tests.ps1, infra/chart-telemetry.Tests.ps1, infra/chart-namespace.Tests.ps1, infra/chart-tms.Tests.ps1, infra/chart-erp.Tests.ps1, infra/chart-driver-app.Tests.ps1, infra/chart-integration-console.Tests.ps1, infra/chart-partner-portal.Tests.ps1'`. They run offline.
