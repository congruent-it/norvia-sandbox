{{/* An image name under the configured registry: {{ include "norvia.image" (dict "root" . "name" "tms-migrations") }} */}}
{{- define "norvia.image" -}}
{{- $registry := .root.Values.image.registry | trimSuffix "/" -}}
{{- if $registry }}{{ $registry }}/{{ end }}{{ .name }}:{{ .root.Values.image.tag }}
{{- end }}

{{- define "norvia.labels" -}}
app.kubernetes.io/part-of: norvia
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end }}

{{/*
  initContainers that hold a pod until the named migrations Jobs have completed:
    initContainers:
      {{- include "norvia.waitForMigrations" (dict "root" . "names" (list "tms")) | nindent 6 }}
  The pod must run as the service account migrations-waiter.
*/}}
{{- define "norvia.waitForMigrations" -}}
{{- range .names }}
- name: wait-for-{{ . }}-migrations
  image: {{ $.root.Values.kubectl.image }}
  # No shell: the kubectl image has none. kubectl fails while the Job does not exist yet or has not completed, and the kubelet
  # retries the init container with a back-off, so the pod waits, and a Job that exhausts its retries keeps the pod from starting.
  command:
    - kubectl
    - wait
    - --for=condition=complete
    - --timeout=60s
    - job/{{ . }}-migrations-{{ $.root.Release.Revision }}
{{- end }}
{{- end }}

{{/* The connection string the apps read for Azurite (ConnectionStrings__blobs, and ConnectionStrings__driver-app-blobs for the Driver app). The key is Azurite's published development key. */}}
{{- define "norvia.azuriteConnectionString" -}}
DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;
{{- end }}

{{/* The connection string an application reads for a Service Bus emulator, as ConnectionStrings__messaging: {{ include "norvia.serviceBusConnectionString" (dict "root" . "name" "messaging-2") }}. Both emulators are asked for as "messaging" by the apps that use them. */}}
{{- define "norvia.serviceBusConnectionString" -}}
{{ include "norvia.serviceBusEndpoint" (dict "host" .name) }}
{{- end }}

{{/* The same for the emulator's management port, which the Integration console reads the entities through (LocalServiceBus__Namespaces__<name>__ManagementConnectionString). */}}
{{- define "norvia.serviceBusManagementConnectionString" -}}
{{ include "norvia.serviceBusEndpoint" (dict "host" (printf "%s:5300" .name)) }}
{{- end }}

{{- define "norvia.serviceBusEndpoint" -}}
Endpoint=sb://{{ .host }};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
{{- end }}

{{/* The Config.json of one emulator: {{ include "norvia.serviceBusConfig" (dict "root" . "name" "messaging") }}. */}}
{{- define "norvia.serviceBusConfig" -}}
{{- $emulator := index .root.Values.serviceBus.emulators .name -}}
{{- $topics := list -}}
{{- range $topic, $subscriptions := $emulator.topics -}}
  {{- $subs := list -}}
  {{- range $subscription := $subscriptions -}}
    {{- $condition := index $.root.Values.serviceBus.conditions $subscription -}}
    {{- $expression := printf "resubmitTo IS NULL OR resubmitTo = '%s'" $subscription -}}
    {{- if $condition }}{{ if contains " OR " $condition }}{{ $condition = printf "(%s)" $condition }}{{ end }}{{ $expression = printf "(%s AND resubmitTo IS NULL) OR resubmitTo = '%s'" $condition $subscription }}{{ end -}}
    {{- $properties := dict "RequiresSession" (has $subscription $.root.Values.serviceBus.sessionSubscriptions) -}}
    {{- with index $.root.Values.serviceBus.lockDurations $subscription }}{{ $_ := set $properties "LockDuration" . }}{{ end -}}
    {{- $sub := dict "Name" $subscription "Properties" $properties "Rules" (list (dict "Name" "resubmit-aware" "Properties" (dict "FilterType" "Sql" "SqlFilter" (dict "SqlExpression" $expression)))) -}}
    {{- $subs = append $subs $sub -}}
  {{- end -}}
  {{- $topics = append $topics (dict "Name" $topic "Subscriptions" $subs) -}}
{{- end -}}
{{- $queues := list -}}
{{- range $queue := $emulator.queues -}}
  {{- $queueProperties := dict "MaxDeliveryCount" 10 -}}
  {{- with index $.root.Values.serviceBus.lockDurations $queue }}{{ $_ := set $queueProperties "LockDuration" . }}{{ end -}}
  {{- $queues = append $queues (dict "Name" $queue "Properties" $queueProperties) -}}
{{- end -}}
{{- dict "UserConfig" (dict "Namespaces" (list (dict "Name" "emulatorNs" "Queues" $queues "Topics" $topics)) "Logging" (dict "Type" "File")) | toPrettyJson -}}
{{- end }}

{{/* The in-cluster address of an application: {{ include "norvia.url" (dict "root" . "name" "tms-api") }}. */}}
{{- define "norvia.url" -}}
http://{{ .name }}:{{ .root.Values.appPort }}
{{- end }}

{{/* The hostname of an application under the base domain: {{ include "norvia.host" (dict "root" . "app" "partner-api") }}. */}}
{{- define "norvia.host" -}}
{{- $app := index .root.Values.apps .app -}}
{{ $app.host }}.{{ required "edge.baseDomain is required: every UI and API gets <name>.<baseDomain>" .root.Values.edge.baseDomain }}
{{- end }}

{{/* A token constant of LocalTokens.cs: {{ include "norvia.token" (dict "root" . "name" "TmsAudience") }}. */}}
{{- define "norvia.token" -}}
{{ index .root.Values.localTokens .name }}
{{- end }}

{{/* An environment variable holding the install's token signing key (base64, as JWT bearer configuration takes it): {{ include "norvia.signingKeyEnv" "Tms__Tokens__SigningKey" }}. */}}
{{- define "norvia.signingKeyEnv" -}}
- name: {{ . }}
  valueFrom:
    secretKeyRef:
      name: local-tokens
      key: signing-key
{{- end }}

{{/* The Persona's client ID, which stands in for its Entra client app. */}}
{{- define "norvia.personaClientId" -}}
norvia-local-persona-{{ . }}
{{- end }}

{{/* The Service of an application on the app port: {{ include "norvia.service" (dict "root" . "name" "partner-api") }}. */}}
{{- define "norvia.service" -}}
apiVersion: v1
kind: Service
metadata:
  name: {{ .name }}
  labels:
    app.kubernetes.io/name: {{ .name }}
    {{- include "norvia.labels" .root | nindent 4 }}
spec:
  selector:
    app.kubernetes.io/name: {{ .name }}
  ports:
    - name: http
      port: {{ .root.Values.appPort }}
      targetPort: http
{{- end }}

{{/* The pod spec's parts every .NET application shares: the pull secrets, the port, the probes of ServiceDefaults (/health, /alive) and the limits. */}}
{{- define "norvia.appContainer" -}}
image: {{ include "norvia.image" (dict "root" .root "name" .app.image) }}
imagePullPolicy: {{ .root.Values.image.pullPolicy }}
ports:
  - name: http
    containerPort: {{ .root.Values.appPort }}
readinessProbe:
  httpGet:
    path: /health
    port: http
  periodSeconds: 5
livenessProbe:
  httpGet:
    path: /alive
    port: http
  initialDelaySeconds: 20
  periodSeconds: 15
resources:
  {{- toYaml .app.resources | nindent 2 }}
{{- end }}

{{/*
  The settings of one token identity (the AppHost's <prefix>__Issuer, __Audience, optionally __ClientId, and the __SigningKey of the install):
    {{- include "norvia.tokenEnv" (dict "root" . "prefix" "Tms__Tokens" "audience" "TmsAudience" "clientId" "IntakeLogicAppClientId") | nindent 12 }}
  audience and clientId name constants of localTokens.
*/}}
{{- define "norvia.tokenEnv" -}}
- name: {{ .prefix }}__Issuer
  value: {{ include "norvia.token" (dict "root" .root "name" "Issuer") | quote }}
- name: {{ .prefix }}__Audience
  value: {{ include "norvia.token" (dict "root" .root "name" .audience) | quote }}
{{- if .clientId }}
- name: {{ .prefix }}__ClientId
  value: {{ include "norvia.token" (dict "root" .root "name" .clientId) | quote }}
{{- end }}
{{ include "norvia.signingKeyEnv" (printf "%s__SigningKey" .prefix) }}
{{- end }}

{{/*
  A one-replica Deployment of an application, with the shared container parts. env is the rendered list of its environment
  variables; secrets names the Secrets it takes as envFrom:
    {{ include "norvia.deployment" (dict "root" . "name" "partner-api" "env" $env "secrets" (list "subscription-keys")) }}
*/}}
{{- define "norvia.deployment" -}}
{{- $app := index .root.Values.apps .name -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ .name }}
  labels:
    app.kubernetes.io/name: {{ .name }}
    {{- include "norvia.labels" .root | nindent 4 }}
spec:
  replicas: 1
  selector:
    matchLabels:
      app.kubernetes.io/name: {{ .name }}
  template:
    metadata:
      labels:
        app.kubernetes.io/name: {{ .name }}
    spec:
      {{- if .root.Values.image.pullSecrets }}
      imagePullSecrets:
        {{- toYaml .root.Values.image.pullSecrets | nindent 8 }}
      {{- end }}
      containers:
        - name: {{ .name }}
          {{- include "norvia.appContainer" (dict "root" .root "app" $app) | nindent 10 }}
          {{- range .secrets | default list }}
          envFrom:
            - secretRef:
                name: {{ . }}
          {{- end }}
          env:
            - name: ASPNETCORE_HTTP_PORTS
              value: {{ .root.Values.appPort | quote }}
            {{- .env | nindent 12 }}
{{- end }}
