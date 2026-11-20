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

## The registry

Set up, and it already holds container images: the packages are listed at <https://git.home.arpa/smirking_squiggly/-/packages>. Pushing to it needs a token with the `write:package` scope (listing needs `read:package`); its address becomes `image.registry` in the chart ([kubernetes.md](kubernetes.md)). Not checked here: how the existing images are named and tagged, and that the cluster's nodes can pull from it. Decided in #1016.

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
