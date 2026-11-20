# Forgejo

How the homelab's Forgejo mirrors this repository and runs workflows on it, and how to check that it does. GitHub stays the **source of truth**: issues, PRs, labels and `Closes #n` live there ([issue-tracker.md](../agents/issue-tracker.md)). Forgejo holds a read-only copy of the git refs and runs CI; it is never pushed to and is not exposed to the Internet.

Written from setting it up on Forgejo 15.0.11 (`15.0.11+gitea-1.22.0`). The commands use `$FORGEJO_URL` (the base URL) and `$FORGEJO_TOKEN`.

## The pull mirror

Forgejo fetches from GitHub on an interval (outbound only). It carries branches and tags, and nothing else: no issues, PRs, labels, milestones, releases or wiki.

Create it with the migrate API. A public repository needs no GitHub credentials; a private one needs a read-only fine-grained token (Contents: read-only) as `auth_token`.

```bash
curl -sS -X POST -H "Authorization: token $FORGEJO_TOKEN" -H 'Content-Type: application/json' \
  "$FORGEJO_URL/api/v1/repos/migrate" -d '{
    "clone_addr": "https://github.com/congruent-it/norvia-sandbox.git",
    "repo_name": "norvia-sandbox", "repo_owner": "<forgejo user>",
    "service": "git", "mirror": true, "mirror_interval": "10m", "private": true,
    "wiki": false, "issues": false, "pull_requests": false,
    "releases": false, "labels": false, "milestones": false }'
```

A new mirror has **Actions switched off**. Turn it on:

```bash
curl -sS -X PATCH -H "Authorization: token $FORGEJO_TOKEN" -H 'Content-Type: application/json' \
  "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox" -d '{"has_actions": true}'
```

### How soon a commit arrives

Every `mirror_interval` (10 minutes here, which is also Forgejo's default minimum, `MIN_INTERVAL` under `[mirror]` in `app.ini`), but that is when the mirror becomes due, not when it syncs: the page showed "synced 16 minutes ago" with this setting. Allow up to about 20 minutes. Probably Forgejo's scheduler (`[cron.update_mirrors]`, `SCHEDULE`, every 10 minutes by default) only checks for due mirrors every 10 minutes, so the wait is 10 to 20 minutes worst case; not verified, check that setting in `app.ini`. To sync now:

```bash
curl -sS -X POST -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox/mirror-sync"
```

Both manual syncs we tried had the commits within 45 seconds. A GitHub webhook would be faster but needs Forgejo reachable from the Internet, which is ruled out; a shorter interval or a poller on the homelab are the ways to speed this up if the wait hurts.

## What a sync triggers

**A mirror sync starts `push` workflows**, for a new branch and for new commits on a branch that is already mirrored (tried with a throwaway workflow). So no `schedule:` trigger and no GitHub-to-Forgejo call is needed to start a build.

- Workflows live in `.forgejo/workflows/` and must be committed on GitHub; the mirror is read-only.
- `pull_request` events never fire: the mirror has no PRs. A PR gate runs on `push` for the branch.
- Not tried: whether the automatic interval sync (as opposed to the API call) starts workflows, and the `schedule:` trigger.
- Deleting a branch on GitHub removes it from the mirror at the next sync.

## The runner

A runner labelled `docker` picks up jobs: `runs-on: docker`. Check with a workflow that echoes `${{ github.event_name }}`, `${{ github.ref }}` and `${{ github.sha }}`.

Not yet checked: that the runner can build a container image (Docker or Buildah) or run Node for the Tailwind stylesheet. The runner list needs admin access (the `read:admin` scope), so the label above was found by trying it.

### Runner for CI

The gates ([#1030](https://github.com/congruent-it/norvia-sandbox/issues/1030)) need more than the image build: the integration and UI test legs start the AppHost, which runs SQL Server and the Service Bus emulator in Docker (Testcontainers, DCP) and Playwright's Chromium. [#1038](https://github.com/congruent-it/norvia-sandbox/issues/1038) defines the runner for them. **Status: the files are in the repository; nothing here has been run on the homelab yet.** Each "not yet" below is a check still to do, and the numbers are left to fill in.

| What | Where |
|---|---|
| Job image | [.forgejo/runner-image/Dockerfile](../../.forgejo/runner-image/Dockerfile) |
| Per-job Docker daemon | [.forgejo/runner-image/start-dockerd.sh](../../.forgejo/runner-image/start-dockerd.sh), installed as `start-dockerd` |
| Runner config | [.forgejo/runner/config.yaml](../../.forgejo/runner/config.yaml) |
| Image rebuild | [.forgejo/workflows/runner-image.yml](../../.forgejo/workflows/runner-image.yml) |
| Check | [.forgejo/workflows/runner-check.yml](../../.forgejo/workflows/runner-check.yml) |

**Each job has its own Docker daemon.** The runner starts the job container privileged and without the host's Docker socket (`docker_host: "-"`; `automount` would mount it, and `start-dockerd` would then find the host daemon and share it). A job that needs Docker runs `start-dockerd` as its first step, which starts a `dockerd` inside the job container. The containers, networks and ports of one leg are invisible to another, so parallel AppHosts cannot clash on names or ports, and the daemon goes away with the job. `/var/lib/docker` is a volume of the job image, so `dockerd` can use overlay2 and not fall back to the slow vfs driver. The cost: every job pulls its images (SQL Server, the Service Bus emulator) into an empty daemon.

**The job image** is the .NET SDK image (the floating `10.0` tag: `global.json` allows any 10.0 feature band from 10.0.400, and a rebuild takes the current one), PowerShell and the tools of [tool-versions.psd1](../../infra/tool-versions.psd1) (`yq`, and the PSScriptAnalyzer and Pester modules the `powershell` job needs, among them; installed as the dev container does, with `infra/install-powershell.sh` and `infra/install-tools.ps1`), `jq`, git and Docker. `runner-image.yml` builds it on the homelab and pushes it to `<registry>/norvia-ci`, tagged with the commit and `latest`. It runs when the image folder, `tool-versions.psd1`, the two install scripts or `global.json` changes, so a bumped tool version rebuilds the image. It needs the Forgejo variable `REGISTRY` (the image prefix, host and owner, e.g. `git.home.arpa/smirking_squiggly`) and the secrets `REGISTRY_USER` and `REGISTRY_TOKEN` (`write:package`). The first image is built by hand (`docker build -f .forgejo/runner-image/Dockerfile -t <registry>/norvia-ci:latest .` from the repository root, then `docker push`), because the runner's label points at it.

**The NuGet cache** is the named Docker volume `norvia-nuget`, mounted at `/nuget-cache` (`NUGET_PACKAGES`) in every job. It lives on the runner host and survives between runs. Parallel jobs share it.

**Set up the runner.** Installing and running it belongs to the homelab's K3s setup, not to this repository. What that setup must give the runner:

- **A runner of Forgejo runner 13 or later**, created in Forgejo (the mirror's Settings, Actions, Runners, **Create new runner**, named `norvia-ci`). Runner 13 deprecates `register`; the runner is declared as a connection in its config, with the UUID and token Forgejo shows. Keep the token in a secret, not in git.
- **The settings of [.forgejo/runner/config.yaml](../../.forgejo/runner/config.yaml):** the label `norvia-ci:docker://<registry>/norvia-ci:latest` (jobs say `runs-on: norvia-ci`; #1030 had chosen `docker`, which the older runner of #1024 keeps), `capacity`, privileged job containers, `docker_host: "-"` and the `norvia-nuget` volume.
- **A Docker daemon for the runner to start job containers in**, trust for the `git.home.arpa` certificate (to reach Forgejo and to pull `norvia-ci`), and persistent storage for the daemon, so images and `norvia-nuget` survive restarts.
- **The first image in the registry**, pushed by hand, because the label points at it.

**Check it.** Run the `runner-check` workflow (Actions, runner-check, Run workflow). It prints the versions of `dotnet`, `pwsh`, `node`, `jq`, `yq`, git and helm, runs `docker run hello-world` and runs `dotnet test` on the Trips integration test project. Not yet run.

**Add a second runner.** Create another runner in Forgejo (its own UUID and token) in the K3s setup, with the same `norvia-ci` label and its own `norvia-nuget` volume. Jobs go to whichever runner is free; total capacity is the sum of the runners' `capacity`.

**Capacity.** Not yet measured. To fill in: the host's CPU, memory and disk; the `capacity` it runs at; the wall-clock time of one integration leg and one UI leg; and the time of the whole matrix of 44 legs (legs times leg time, divided by capacity, plus image pulls).

**Findings still to record** for the port issues:

- Whether `Aspire.Hosting.Testing` starts on this runner. DCP needs the daemon socket and a way to reach published ports (`host.docker.internal` or an equivalent). If the Aspire test fails in `runner-check`, write what is missing.
- Whether `dockerd` starts in the privileged job container, and that its storage driver is overlay2 (the "Storage Driver" line of `docker info`), not vfs.
- **Resolved: `actions/checkout` resolves from `data.forgejo.org`, but `v5` does not run.** Runner v6.4.0 accepts `node12`, `node16`, `node20` and others up to `node20`, and `v5` declares `node24` ("The runs.using key in action.yml must be one of: [composite docker node12 node16 node20 go sh], got node24"). The workflows use `actions/checkout@v4` (`v4.4.0` declares `node20`). Use `@v4` for the other `actions/*` too, and check each declares `node20` or lower. The build job does not use `setup-dotnet`, `setup-node` or `cache` (see "The build job" under CI). Still to check that `actions/upload-artifact@v3` exists on `data.forgejo.org` and runs.
- That the Playwright Chromium install (`playwright.ps1 install --with-deps chromium`) works in the job image.

## CI

The gates run here and report to GitHub; `ci.yml` has the repo-level jobs `skeleton`, `powershell`, `build` and `gate`, and each deployable has a workflow of its own with the test matrix (see [One workflow per deployable](#one-workflow-per-deployable)); the PowerShell scripts gate, [#1040](https://github.com/congruent-it/norvia-sandbox/issues/1040); the build gate, [#1041](https://github.com/congruent-it/norvia-sandbox/issues/1041); both check that the runner image has the pinned tools and do not install them; the test matrix, [#1042](https://github.com/congruent-it/norvia-sandbox/issues/1042), and the GitHub CI is gone ([ADR 0055](../adr/0055-ci-runs-on-the-homelabs-forgejo-on-push-and-reports-to-github-as-commit-statuses.md), [#1030](https://github.com/congruent-it/norvia-sandbox/issues/1030)). The repo-level workflow is [.forgejo/workflows/ci.yml](../../.forgejo/workflows/ci.yml) and each deployable's is its own file; all have `on: push` for all branches, `concurrency: <workflow>-${{ github.ref }}` with `cancel-in-progress`, and one job per gate (in an application workflow the test gate is one matrix job, `integration`, with a leg per kind of test). Each job brackets its work with the composite action [report-status](../../.forgejo/actions/report-status/action.yml): `pending` first, then `job.status` last under `if: always()`. It posts `forgejo/<job>` to the commit on GitHub with a link to the Forgejo run, using the secret `GH_STATUS_TOKEN` ([the token](#the-github-status-token)). A failed call fails the job.

**Add a repo-level gate:** add a job to `ci.yml` with `runs-on: norvia-ci` and the same two `report-status` steps, with its own `job:` name (the `gate` job is the one exception, see [One workflow per deployable](#one-workflow-per-deployable)). A gate of one application is a leg in its workflow.

**What a PR shows.** The statuses of its head commit, under "Checks". `pending` appears once the run starts, which is after the mirror sync (up to about 20 minutes after the push, see [How soon a commit arrives](#how-soon-a-commit-arrives); Sync now skips the wait). When a newer push cancels a run, the closing step posts `error` ("Cancelled: superseded by a newer push") on the old commit, and the PR shows the new head's statuses. A `pending` that is never replaced stays on the old commit only, and the PR does not show it ([ADR 0055](../adr/0055-ci-runs-on-the-homelabs-forgejo-on-push-and-reports-to-github-as-commit-statuses.md#consequences)).

**Known limits of the gates.** `pending` is posted after `actions/checkout`, because the action is read from the checkout; a failed checkout posts nothing at all (seen: both `report-status` steps were skipped), so the commit shows no `forgejo/<job>` (`skeleton`, `powershell`, `build`) rather than a failure. Missing is not green, but only a required-status rule (a later issue) would make it block a merge. Not yet checked against a real run: that `target_url` (`<server>/<repo>/actions/runs/<run_number>`) opens the Forgejo run.

**Check it end to end.** Push a branch to GitHub and open a PR, then Sync now (or wait). Expect `forgejo/skeleton`, `forgejo/powershell` and `forgejo/build` `pending` and then `success` on the commit and in the PR. Then push twice in a row to see the first run cancelled. **Status: not run yet on the homelab**; record here whether Forgejo runs the `if: always()` step of a cancelled job.

### The build job

`build` ([#1041](https://github.com/congruent-it/norvia-sandbox/issues/1041)) has these steps: restore, a warnings-as-errors build, `dotnet format --verify-no-changes`, the estate's Bicep build and lint, the vulnerable-package check, the check that every test project is claimed by a leg, the unit tests of the Integration functions, Branding, Docs and ServiceDefaults, the coverage summary and the test artifacts. It differs where Forgejo or the runner image does:

- **No installs.** [.forgejo/actions/setup](../../.forgejo/actions/setup/action.yml) The image has the .NET SDK, Node (for the Tailwind build) and the pinned tools, so the action only checks them and fails with the versions it found when the image is stale (rebuild it, see "Runner for CI"). There is no `setup-dotnet`, `setup-node` or `cache` step.
- **NuGet cache.** `NUGET_PACKAGES` is the image's `/nuget-cache`, a volume the runner mounts, so packages survive between runs. This replaces the GitHub job's workspace folder and `actions/cache`, which would restore from and save to Forgejo's cache for nothing a volume does not already keep. The npm caches of the Tailwind builds are not kept: `npm ci` downloads on each run until a measured run says it matters.
- **Bicep** is the pinned CLI of `infra/tool-versions.psd1` (`bicep build` and `bicep lint`), not `az bicep install`: the dev container, the runner and the estate's architecture tests then agree, and the image has no Azure CLI.
- **The steps are bash.** ADR 0048 makes scripts PowerShell; these steps (`jq`, `yq`, `find`) were copied unchanged from the GitHub job, which is gone.
- **The claimed check reads the application workflows**: the `DIRECTORY` of the `integration` job in `.forgejo/workflows/*.yml` (see [One workflow per deployable](#one-workflow-per-deployable)).
- **Artifacts and summary.** `actions/upload-artifact@v4` needs GitHub's artifact service, which Forgejo does not offer, so the job uses `@v3`. Forgejo has no `$GITHUB_STEP_SUMMARY` page, so the coverage Markdown summary goes to the log, and `TestResults/coverage` stays in the artifact. Not yet run on the homelab: that `@v3` uploads and that the artifact opens in the Forgejo run.

**Check it end to end.** On a branch, add one of each: a compiler warning, a formatting error, a Bicep lint error, a vulnerable package and a test project outside every leg. Expect `forgejo/build` to fail on each and to pass without them. **Status: not run yet on the homelab.**

### One workflow per deployable

[#1079](https://github.com/congruent-it/norvia-sandbox/issues/1079) splits the test matrix so that a change runs only what it affects. A deployable (an API with its UI, the Integration estate, the Health check) has its own workflow, named for the key it has in [.forgejo/ci-scope.psd1](../../.forgejo/ci-scope.psd1): `tms`, `partner-portal`, `erp`, `driver-app`, `simulator`, `integration-console`, `integration` and `health-check` (`.forgejo/workflows/<key>.yml`). `ci.yml` keeps what is not one application's: `skeleton`, `powershell`, `build` (build, format, Bicep, vulnerable packages, the claimed check, and the unit tests of Branding, Docs and ServiceDefaults) and `gate`. `images.yml` and `build-image.yml` are unchanged: they are already one workflow per image.

- **Always run, so no status is missing.** Every application workflow starts on every push, and its `scope` job asks the [scope action](../../.forgejo/actions/scope/action.yml) whether the change affects the application. If not, the `integration` matrix is skipped and the closing `result` job reports `forgejo/<key>` as a pass, "Not affected by this change". A path filter (`on.push.paths`) would leave no status at all, which a required-status rule reads as pending. The cost is a `scope` job of a few seconds on a runner slot.
- **What affects what.** [scope.ps1](../../.forgejo/actions/scope/scope.ps1) diffs HEAD against its merge-base with `origin/main` (the checkout has all history) and applies the rules of `ci-scope.psd1`: a file under an application's `Paths`, or under a `Shared` entry that lists it, affects it; a file nothing claims affects **every** application unless `Ignored` lists it. So `infra/`, `contracts/`, `seed/`, `agents/`, `evaluation/`, `Directory.*.props`, `global.json` and any new top-level folder run everything, and `Norvia.ServiceDefaults`, `Norvia.Branding`, `Norvia.Agents` and the AppHost run their consumers. Docs, the GitHub files and the other workflows (`images.yml` and so on) are ignored; `docs/architecture/*`, `docs/processes/*` and `docs/operations/agent-evaluation.md` affect the applications whose architecture tests read them.
- **On `main` everything runs.** The AppHost starts the whole estate, so an application's tests also exercise its neighbours; a change in a neighbour runs the neighbour's workflow only (except the direct project references, which `ci-scope.psd1` lists). `main` runs every workflow and catches what that misses. The same applies when the merge-base cannot be found.
- **The manifest is tested.** `scope.Tests.ps1` runs in the `powershell` gate: every key has a workflow file, every top-level folder of `src/` and `tests/` is named in the manifest, and every project reference between applications (except through the AppHost) is covered, so a new reference that the path lists miss fails the gate. The `build` job fails a test project under `tests/` that no workflow's `integration` job runs, as the claimed-legs check did.
- **Statuses.** A leg reports `forgejo/tests (<application>)` as before. A workflow reports `forgejo/<key>` last, when its legs are done (the rolled-up `forgejo/tests` is gone). The `gate` job of `ci.yml` reports `pending` for `forgejo/ci` at once. **Nothing waits**: the `powershell` and `build` jobs, the `gate` job and the `result` job of every application workflow end with the [gate action](../../.forgejo/actions/gate/action.yml), which reads GitHub's combined status of the commit once and reports `forgejo/ci` as `success` when `forgejo/powershell`, `forgejo/build` and every `forgejo/<key>` have succeeded, as `failure` at the first that failed, and not at all while one is still to report. So the last job to finish reports success. A workflow that never starts leaves `forgejo/ci` pending, which a required-status rule reads as not passed. **Require `forgejo/ci` alone** in branch protection (the optional step of [#1048](https://github.com/congruent-it/norvia-sandbox/issues/1048)): a new workflow joins it by getting a key in the manifest, with no change to the rule. (A first version had a job poll until the others reported. It held a runner slot the queued workflows needed and deadlocked a push to `main`, so it was replaced.)
- **A new application** takes: a folder in `src/` and `tests/`, a key with `Paths` in `ci-scope.psd1` (and a `Shared` entry for every other application's code its tests reference), and a `.forgejo/workflows/<key>.yml` copied from a small one such as `driver-app.yml` (the `scope`, `integration` and `result` jobs, with its legs). The steps of a leg are the same in every workflow and use the job's `DIRECTORY` (the folder of `tests/`); they are plain steps, not a composite action, because Forgejo shows a composite action as one row with no log until it ends. A change to a leg's steps is a change to all eight files.
- **Status: not run on the homelab yet.** To check: a docs-only branch reports `Not affected` for all eight workflows and `forgejo/ci` goes to `success` within about a minute; a change under `src/Erp` runs `erp` (and `integration`, which references it) and no other; a change to `global.json` runs all; a failing test in one leg fails `forgejo/<key>` and `forgejo/ci`; and a push to `main` runs all eight. Also unconfirmed on the runner: that a composite action's outputs (`scope`) and a job's `outputs` work in Forgejo's runner, and that a status posted a moment before is visible to the next job's read, and the real time saved.

### The test matrix

The matrix below is now split across the workflows above; each workflow's `integration` job has the legs of its application, and a leg's steps are the same in each file. The rest of this section still describes a leg.

`integration` ([#1042](https://github.com/congruent-it/norvia-sandbox/issues/1042)) is the test gate: a `matrix.include` of legs, each with an application, its test `directory` and the `kinds` of test project it runs. It has all 44 legs of GitHub's matrix ([#1043](https://github.com/congruent-it/norvia-sandbox/issues/1043)): TMS (24), Partner portal (5), ERP (3), Driver app, Simulator (7), Integration console, Integration (2) and Estate health check, each with the same `application`, `directory` and `kinds`, and `fail-fast: false`. Each leg runs: report `pending`, `start-dockerd`, the setup check, build, Playwright's Chromium (when the kinds include `UiTests`), the tests with `NORVIA_TEST_LOG_FILE`, `NORVIA_UI_TEST_ARTIFACTS` and the DCP diagnostics variables, the coverage summary, the `TestResults` artifact, and the closing status.

- **Whole-PR check, still to do.** A branch with a deliberately failing test in each application's leg should fail exactly that leg on both CI systems ([#1043](https://github.com/congruent-it/norvia-sandbox/issues/1043)). It needs the homelab runner and a run of the whole matrix, so it has not been run.
- **Queueing.** The runner runs `capacity` jobs at once ([config.yaml](../../.forgejo/runner/config.yaml)); the other legs wait for a slot and a job's `timeout-minutes` counts from when it starts, so the 30 minutes stay what one leg needs. Raise it only for a leg measured to need more.

- **Root.** The job runs as root, the job image's default user (the Dockerfile sets no `USER`), so `playwright.ps1 install --with-deps chromium` can install Chromium's system libraries as it does on GitHub's runner, and `start-dockerd` can start `dockerd`. The image is unchanged for this; a non-root user would need `sudo` (installed) in front of both.
- **Docker.** The first step is `start-dockerd`, so every leg has its own daemon ([Runner for CI](#runner-for-ci)). **Observed on the first runs (#1079): the daemon was the runner pod's own** (`Name: norvia-ci-0`, API on `tcp://0.0.0.0:2375`, the job container in `docker ps`), because `start-dockerd` exits when `docker info` already answers, so the legs shared one daemon and its image cache; check how the runner's `docker_host` is set on the homelab. The legs that start the Service Bus emulators (Driver app, ERP, Integration console, Partner portal) timed out in the AppHost fixture's 10-minute startup, with SQL Server and Azurite cached and the emulator image absent. A step pulls the three images with their time before the build, and a failed leg's *Docker diagnostics* step prints the daemon's recent events.
- **Statuses.** Each leg reports `forgejo/tests (<application>)` (e.g. `forgejo/tests (Estate health check)`), so each can become required by itself. Each workflow's `result` job rolls its legs up as `forgejo/<key>`, and `forgejo/ci` rolls up the workflows ([One workflow per deployable](#one-workflow-per-deployable)). Require `forgejo/ci`; require a leg's own status only to gate on that leg by itself.
- **Differences from GitHub.** `actions/checkout@v4` and `actions/upload-artifact@v3`, and the coverage Markdown summary goes to the log, all as in [the build job](#the-build-job).
- **Timing.** Not measured: the leg has not run on the homelab. To fill in, from the first run: the leg's duration here, and how long the image pulls and the Chromium install take.

**Check it end to end.** Push a branch that changes `src/HealthCheck` and expect `forgejo/tests (Estate health check)`, `forgejo/health-check` and `forgejo/ci` to go to `success` on the commit. Break a test and expect all three to fail. **Status: not run yet on the homelab**; this also answers the open questions of [Runner for CI](#runner-for-ci) (`dockerd`, Aspire, Chromium, `upload-artifact@v3`).

## The registry

Set up, and it already holds container images (the application images of [The images](#the-images) go there too): the packages are listed at <https://git.home.arpa/smirking_squiggly/-/packages>. Pushing to it needs a token with the `write:package` scope (listing needs `read:package`); its address becomes `image.registry` in the chart ([kubernetes.md](kubernetes.md)). Not checked here: how the existing images are named and tagged, and that the cluster's nodes can pull from it. Decided in #1016.

### The registry push token

[runner-image.yml](../../.forgejo/workflows/runner-image.yml) pushes the `norvia-ci` job image with these settings on the mirror (Settings → Actions):

| Name | Kind | Value |
|---|---|---|
| `REGISTRY` | Variable | `git.home.arpa/smirking_squiggly`, the image prefix (host and owner); the workflow logs in to the host part only |
| `REGISTRY_USER` | Secret | `smirking_squiggly` |
| `REGISTRY_TOKEN` | Secret | The value of the Forgejo access token **`docker-registry-push`** (scope `write:package`) |

`REGISTRY_TOKEN` is a copy of that token, not a separate credential. When `docker-registry-push` is rotated or expires, paste the new value into `REGISTRY_TOKEN` too, or the `docker login` of runner-image.yml fails (the image workflows below log in with it through the SDK, and fail the same way). The same token serves a manual push (`docker login git.home.arpa -u smirking_squiggly`).

## The images

[images.yml](../../.forgejo/workflows/images.yml) builds the applications' images and pushes them to the registry, with the reusable [build-image.yml](../../.forgejo/workflows/build-image.yml) ([#1044](https://github.com/congruent-it/norvia-sandbox/issues/1044), [ADR 0056](../adr/0056-images-are-built-with-publishcontainer-on-the-homelabs-forgejo-and-tagged-by-commit.md)). **Status: nothing here has been run on the homelab yet, and how the images already in the registry are named and tagged was not checked (the names follow `norvia-ci`: `<REGISTRY>/<name>`).**

The applications' images, each one `uses:` in `images.yml` ([#1045](https://github.com/congruent-it/norvia-sandbox/issues/1045), [#1046](https://github.com/congruent-it/norvia-sandbox/issues/1046)), plus the workers and stand-ins below: the seven web apps (below), the APIs `tms-api`, `erp-api` and `driver-app-api`, and the migrations `tms-migrations`, `erp-migrations`, `driver-app-migrations`, `partner-portal-migrations` and `simulator-migrations`, the names `migrations.yaml` and the other chart templates use (`infra/image-workflow.Tests.ps1` compares them with `values.yaml` and the project folders).

The migrations projects use the Worker SDK, so `build-image.yml` passes `EnableSdkContainerSupport=true`; the image is still the ASP.NET base image. A later application image is one more `uses:`.

The integration workers and the local stand-ins ([#1047](https://github.com/congruent-it/norvia-sandbox/issues/1047)) are one job, `integration`, with a matrix entry each: every `Norvia.Integration.Local*Worker` project (`booking-worker`, `billing-worker`, ... the keys of `workers` in `values.yaml`), `partner-api` (`LocalPartnerApi`) and `agent-mcp` (`LocalAgentMcp`), the names `workers.yaml`, `partner-api.yaml` and `agent-mcp.yaml` give their images. `fail-fast` is off, so one project that does not build fails its own `forgejo/image (<name>)` and not the others. Each entry is a run of `build-image.yml` and publishes its own project, so the restore is not one step: the runner's NuGet volume holds the packages and each entry finds them there. A single restore with sixteen publishes would need one job reporting sixteen statuses, and the reusable workflow does one image each. `infra/image-workflow.Tests.ps1` fails when a worker or stand-in project has no entry, when the chart names an image nothing builds (it reads `helm template`), and when the workers are not one matrix.

The seven Blazor web apps are `tms-web`, `erp-web`, `driver-app-web`, `integration-console-web`, `partner-portal-web`, `simulator-web` and `health-check-web`. Five of them build a Tailwind stylesheet (TMS UI [ADR 0018](../adr/0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md), Partner portal [0022](../adr/0022-the-partner-portal-is-styled-with-tailwind-in-luxs-look.md), Integration console and the Simulator [0025](../adr/0025-the-integration-console-is-styled-with-tailwind-in-darklys-look.md), ERP [0044](../adr/0044-the-erp-ui-is-styled-with-tailwind-in-ceruleans-look.md)) and are called with `needs-node: true`; the Driver app and the Health check have no `package.json` and need no Node. The stylesheet is built by the project's own MSBuild target (`npm ci`, then `npm run build:css`) during `dotnet publish`, which the runner image's Node serves; `app.css` and the fonts are published content and `node_modules` is removed from it, so an image holds the built stylesheet and no `node_modules` (`infra/image-workflow.Tests.ps1` checks the flag against the `package.json` files and the `Content Remove`).

| Ref pushed | Tags |
|---|---|
| `main` | the 7-character commit SHA and `latest` |
| a `v*` tag | the SHA and the tag name |
| a branch named `image/*` | the SHA only |
| any other branch | no image |

The job reports `forgejo/image (tms-api)` to the commit on GitHub. It uses the settings of [the registry push token](#the-registry-push-token) and the status token. `dotnet publish /t:PublishContainer` logs in with them directly, so there is no `docker login`.

**Check it.** Push a branch named `image/check-images`, then:

1. `forgejo/image (<name>)` goes to `success` on the commit, for each image in `images.yml`. A certificate error at `Publish the image` means the job image does not trust `git.home.arpa`.
2. Each package, tagged with the first 7 characters of the commit, is listed on the registry's packages page.
3. From a node of the cluster: `docker pull git.home.arpa/smirking_squiggly/tms-api:<sha>` (or `crictl pull`), and the image is `linux/amd64`.
4. `helm template norvia deploy/helm/norvia --set image.registry=git.home.arpa/smirking_squiggly --set image.tag=<sha> --set edge.baseDomain=norvia.test` names `git.home.arpa/smirking_squiggly/<name>:<sha>` for the migrations images, the workers, `partner-api` and (with `--set agents.enabled=true`) `agent-mcp`; the API and web images join them when the chart slices that deploy them land (#1017, #1018, #1019 and later) (`infra/image-workflow.Tests.ps1` checks the shape, and that every image the chart names under the registry has a caller).
5. Push the same SHA again from a clean checkout and run the images: a web app, started with the local sign-in off, serves its start page (`/`; the Health check and the Driver app their `/health` endpoint too) and `/app.css`, and has no `node_modules` folder; an API answers its health endpoint (`/health`) without a database; a migrations image starts and, with no SQL Server to reach, fails on the connection, not on a missing file (a migrations image has no `--help`: it is a host that migrates, then exits). The digest differs between builds; the inputs are the same.

## The API token

Scopes used above, and nothing else (each was not tried on its own, so this is a working set, not a proven minimum):

| Scope | For |
|---|---|
| `write:repository` | Creating the mirror, enabling Actions, `mirror-sync` |
| `read:repository` | Listing workflow runs (`GET /repos/<owner>/<repo>/actions/tasks`) |
| `read:user` | `GET /api/v1/user`, to confirm the token |

Forgejo has no Actions scope. Give the token a short expiry, keep it in an environment variable, and never put it in a command line that is logged.

## The GitHub status token

The gates report their results to GitHub as commit statuses ([#1030](https://github.com/congruent-it/norvia-sandbox/issues/1030)). Forgejo needs a credential for that outbound call, kept apart from the mirror's own read-only token.

| | |
|---|---|
| GitHub token | Fine-grained personal access token `forgejo-ci-commit-statuses` |
| Scope | Resource owner `congruent-it`, only `congruent-it/norvia-sandbox`; **Commit statuses: read and write**, plus the Metadata read that GitHub adds. Nothing else |
| Forgejo secret | `GH_STATUS_TOKEN` on the mirror (Settings → Actions → Secrets) |
| Owner | smirkingsquiggly |
| Expires | 18 February 2027 |

Forgejo rejects secret names that start with `FORGEJO_`, `GITEA_`, `GITHUB_` or a digit, which is why the secret is `GH_STATUS_TOKEN` and not `GITHUB_STATUS_TOKEN`.

**Rotate** before the expiry date:

1. On GitHub, Settings → Developer settings → Personal access tokens → Fine-grained tokens → `forgejo-ci-commit-statuses` → **Regenerate token**. Pick a new expiry and write it in the table above.
2. Replace the value of `GH_STATUS_TOKEN` in Forgejo.
3. Run the check below.

**Check** that the token can post a status. Use the SHA of a throwaway commit, and put the token in `$GH_STATUS_TOKEN` without logging it. Expect `201`, then see the `forgejo/token-check` status on that commit on GitHub. `403` or `404` means the permission or the repository selection is wrong.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST \
  -H "Authorization: Bearer $GH_STATUS_TOKEN" -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/congruent-it/norvia-sandbox/statuses/<sha> \
  -d '{"state":"success","context":"forgejo/token-check","description":"Token check"}'
```

## Checking it

```bash
curl -sS "$FORGEJO_URL/api/v1/version"                                   # the server answers
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/user"   # the token works
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox/branches"   # the refs arrived
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox/actions/tasks"   # the runs
```
