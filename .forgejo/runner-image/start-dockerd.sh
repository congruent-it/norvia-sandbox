#!/usr/bin/env bash
# Starts this job's own Docker daemon and waits until it answers. Run it as the first step of a job that needs Docker (the integration and UI
# test legs, the image build). The job container runs privileged (.forgejo/runner/config.yaml) and has no host socket, so the daemon, its
# containers, networks and ports belong to this job alone and disappear with it.
set -euo pipefail

if docker info >/dev/null 2>&1; then exit 0; fi

# The storage driver is left to the daemon. /var/lib/docker is a volume (see the Dockerfile), so overlay2 should work; check the "Storage Driver" line of `docker info`.
nohup dockerd --host=unix:///var/run/docker.sock >/var/log/dockerd.log 2>&1 &

for _ in $(seq 1 60); do
  if docker info >/dev/null 2>&1; then exit 0; fi
  sleep 1
done
echo "dockerd did not start within 60 s:" >&2
tail -n 50 /var/log/dockerd.log >&2
exit 1
