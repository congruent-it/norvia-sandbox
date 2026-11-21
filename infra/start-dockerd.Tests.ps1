#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# start-dockerd (#1079): a job's own Docker daemon, even where the runner's daemon already answers. Seam: the script run with fake `docker` and `dockerd`
# programs first on the PATH; the real ones need a privileged container.
BeforeAll {
    $script:script = Join-Path (git -C $PSScriptRoot rev-parse --show-toplevel) '.forgejo/runner-image/start-dockerd.sh'
    $script:bin = Join-Path $TestDrive 'bin'
    New-Item -ItemType Directory $script:bin | Out-Null

    # `docker info` answers when DOCKER_HOST is the job's socket and the fake dockerd was started, or, with no DOCKER_HOST, when SHARED_DAEMON is set:
    # the runner pod's daemon, which the job reaches through /var/run/docker.sock.
    Set-Content (Join-Path $script:bin 'docker') @'
#!/usr/bin/env bash
[ "$1" = info ] || exit 0
if [ "${DOCKER_HOST:-}" = "unix://$JOB_DOCKER_SOCKET" ] && [ -e "$FAKE_STATE/started" ]; then exit 0; fi
if [ -z "${DOCKER_HOST:-}" ] && [ -n "${SHARED_DAEMON:-}" ]; then exit 0; fi
exit 1
'@
    Set-Content (Join-Path $script:bin 'dockerd') @'
#!/usr/bin/env bash
echo "$@" >> "$FAKE_STATE/dockerd-args"
[ -n "${DOCKERD_NEVER_ANSWERS:-}" ] || touch "$FAKE_STATE/started"
echo "dockerd log line"
'@
    chmod +x (Join-Path $script:bin 'docker') (Join-Path $script:bin 'dockerd')

    function script:Start-Dockerd([hashtable] $Environment = @{}) {
        $state = Join-Path $TestDrive ([guid]::NewGuid())
        New-Item -ItemType Directory $state | Out-Null
        $githubEnv = Join-Path $state 'github-env'
        $variables = @{
            PATH = "$($script:bin):$env:PATH"; FAKE_STATE = $state; JOB_DOCKER_SOCKET = "$state/job-docker.sock"
            DOCKERD_LOG = "$state/dockerd.log"; GITHUB_ENV = $githubEnv; START_DOCKERD_TIMEOUT = '2'
        } + $Environment
        $previous = @{}
        foreach ($name in $variables.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $variables[$name]) }
        try { $output = bash $script:script 2>&1; $exit = $LASTEXITCODE }
        finally { foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
        [pscustomobject]@{
            ExitCode = $exit; Output = "$output"; Socket = "$state/job-docker.sock"
            DockerdArgs = (Test-Path "$state/dockerd-args") ? (Get-Content "$state/dockerd-args" -Raw) : ''
            GithubEnv = (Test-Path $githubEnv) ? (Get-Content $githubEnv -Raw) : ''
        }
    }
}

Describe 'start-dockerd' {
    It 'starts its own daemon on the job socket although the runner daemon answers' {
        $run = Start-Dockerd @{ SHARED_DAEMON = '1' }

        $run.ExitCode | Should -Be 0
        $run.DockerdArgs | Should -BeLike "*--host=unix://$($run.Socket)*"
    }

    It 'points later steps at that daemon through GITHUB_ENV' {
        $run = Start-Dockerd @{ SHARED_DAEMON = '1' }

        $run.GithubEnv.Trim() | Should -Be "DOCKER_HOST=unix://$($run.Socket)"
    }

    It 'gives the daemon an address pool that cannot overlap the job container network' {
        (Start-Dockerd).DockerdArgs | Should -BeLike '*--default-address-pool*base=10.200.0.0/16*'
    }

    It 'fails with the daemon log when the daemon does not answer in time' {
        $run = Start-Dockerd @{ DOCKERD_NEVER_ANSWERS = '1' }

        $run.ExitCode | Should -Not -Be 0
        $run.Output | Should -BeLike '*dockerd log line*'
    }
}
