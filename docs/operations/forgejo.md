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

Every `mirror_interval` (10 minutes here, which is also Forgejo's default minimum, `MIN_INTERVAL` under `[mirror]` in `app.ini`), so a commit lands within 0 to 10 minutes. To sync now:

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

**Each job has its own Docker daemon.** The runner starts the job container privileged and without the host's Docker socket (`docker_host: "-"`; an empty value would mount it, and `start-dockerd` would then find the host daemon and share it). A job that needs Docker runs `start-dockerd` as its first step, which starts a `dockerd` inside the job container. The containers, networks and ports of one leg are invisible to another, so parallel AppHosts cannot clash on names or ports, and the daemon goes away with the job. `/var/lib/docker` is a volume of the job image, so `dockerd` can use overlay2 and not fall back to the slow vfs driver. The cost: every job pulls its images (SQL Server, the Service Bus emulator) into an empty daemon.

**The job image** is the .NET SDK image (the floating `10.0` tag: `global.json` allows any 10.0 feature band from 10.0.400, and a rebuild takes the current one), PowerShell and the tools of [tool-versions.psd1](../../infra/tool-versions.psd1) (`yq` among them; installed as the dev container does, with `infra/install-powershell.sh` and `infra/install-tools.ps1`), `jq`, git and Docker. `runner-image.yml` builds it on the homelab and pushes it to `<registry>/norvia-ci`, tagged with the commit and `latest`. It runs when the image folder, `tool-versions.psd1`, the two install scripts or `global.json` changes, so a bumped tool version rebuilds the image. It needs the Forgejo variable `REGISTRY` (the image prefix, host and owner, e.g. `git.home.arpa/smirking_squiggly`) and the secrets `REGISTRY_USER` and `REGISTRY_TOKEN` (`write:package`). The first image is built by hand (`docker build -f .forgejo/runner-image/Dockerfile -t <registry>/norvia-ci:latest .` from the repository root, then `docker push`), because the runner's label points at it.

**The NuGet cache** is the named Docker volume `norvia-nuget`, mounted at `/nuget-cache` (`NUGET_PACKAGES`) in every job. It lives on the runner host and survives between runs. Parallel jobs share it.

**Set up the runner**

1. Edit `.forgejo/runner/config.yaml`: replace `<registry>` in the label and set `capacity`.
2. Register it with a registration token from Forgejo: `forgejo-runner register --no-interactive --instance "$FORGEJO_URL" --token <token> --name homelab-ci --labels "docker:docker://<registry>/norvia-ci:latest"`.
3. Run `forgejo-runner daemon --config .forgejo/runner/config.yaml` under systemd or as a container.

**Check it.** Run the `runner-check` workflow (Actions, runner-check, Run workflow). It prints the versions of `dotnet`, `pwsh`, `node`, `jq`, `yq`, git and helm, runs `docker run hello-world` and runs `dotnet test` on the Trips integration test project. Not yet run.

**Add a second runner.** Repeat the three steps on another host with a different `--name`, the same label and its own `norvia-nuget` volume. Jobs go to whichever runner is free; total capacity is the sum of the runners' `capacity`.

**Capacity.** Not yet measured. To fill in: the host's CPU, memory and disk; the `capacity` it runs at; the wall-clock time of one integration leg and one UI leg; and the time of the whole matrix of about 50 legs (legs times leg time, divided by capacity, plus image pulls).

**Findings still to record** for the port issues:

- Whether `Aspire.Hosting.Testing` starts on this runner. DCP needs the daemon socket and a way to reach published ports (`host.docker.internal` or an equivalent). If the Aspire test fails in `runner-check`, write what is missing.
- Whether `dockerd` starts in the privileged job container, and that its storage driver is overlay2 (the "Storage Driver" line of `docker info`), not vfs.
- Where `uses: actions/checkout@v5` resolves from. Forgejo's default is `data.forgejo.org`. Check that `actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/cache` and `actions/upload-artifact` are there, and at which versions.
- That the Playwright Chromium install (`playwright.ps1 install --with-deps chromium`) works in the job image.

## The registry

Set up, and it already holds container images: the packages are listed at <https://git.home.arpa/smirking_squiggly/-/packages>. Pushing to it needs a token with the `write:package` scope (listing needs `read:package`); its address becomes `image.registry` in the chart ([kubernetes.md](kubernetes.md)). Not checked here: how the existing images are named and tagged, and that the cluster's nodes can pull from it. Decided in #1016.

### The registry push token

[runner-image.yml](../../.forgejo/workflows/runner-image.yml) pushes the `norvia-ci` job image with these settings on the mirror (Settings → Actions):

| Name | Kind | Value |
|---|---|---|
| `REGISTRY` | Variable | `git.home.arpa/smirking_squiggly`, the image prefix (host and owner); the workflow logs in to the host part only |
| `REGISTRY_USER` | Secret | `smirking_squiggly` |
| `REGISTRY_TOKEN` | Secret | The value of the Forgejo access token **`docker-registry-push`** (scope `write:package`) |

`REGISTRY_TOKEN` is a copy of that token, not a separate credential. When `docker-registry-push` is rotated or expires, paste the new value into `REGISTRY_TOKEN` too, or the workflow's `docker login` fails. The same token serves a manual push (`docker login git.home.arpa -u smirking_squiggly`).

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
