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
