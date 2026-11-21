#!/usr/bin/env bash
# Starts this job's own Docker daemon and waits until it answers. Run it as the first step of a job that needs Docker (the integration and UI
# test legs, the image build). The job container runs privileged (.forgejo/runner/config.yaml), so the daemon runs inside it: its containers, networks
# and published ports belong to this job alone, a container's published port is on the job's own loopback, and everything disappears with the job.
#
# It starts its own daemon even when another one answers. The runner pod's daemon is reachable through /var/run/docker.sock, and a job that used it
# could not reach a container's published port by `localhost` (the probe of the first runs: only the bridge gateway answered), which the AppHost's
# health checks rely on. Later steps reach this daemon through DOCKER_HOST, which the script writes to $GITHUB_ENV.
set -euo pipefail

socket="${JOB_DOCKER_SOCKET:-/var/run/job-docker.sock}"
log="${DOCKERD_LOG:-/var/log/dockerd.log}"
wait_seconds="${START_DOCKERD_TIMEOUT:-60}"
export DOCKER_HOST="unix://$socket"

if ! docker info >/dev/null 2>&1; then
  # The storage driver is left to the daemon. /var/lib/docker is a volume (see the Dockerfile), so overlay2 should work; check the "Storage Driver" line of `docker info`.
  # The address pool keeps this daemon's networks off the job container's own (the runner's bridge, 172.x), which Docker would otherwise have to dodge.
  nohup dockerd --host="unix://$socket" --pidfile="${socket%.sock}.pid" --exec-root="${socket%.sock}" \
    --default-address-pool base=10.200.0.0/16,size=24 >"$log" 2>&1 &

  for _ in $(seq 1 "$wait_seconds"); do
    if docker info >/dev/null 2>&1; then break; fi
    sleep 1
  done
  if ! docker info >/dev/null 2>&1; then
    echo "dockerd did not start within $wait_seconds s:" >&2
    tail -n 50 "$log" >&2
    exit 1
  fi
fi

if [ -n "${GITHUB_ENV:-}" ]; then echo "DOCKER_HOST=$DOCKER_HOST" >> "$GITHUB_ENV"; fi
