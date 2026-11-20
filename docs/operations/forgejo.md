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

Not set up yet. Pushing to the Forgejo container registry needs a token with the `write:package` scope; the result becomes `image.registry` in the chart ([kubernetes.md](kubernetes.md)). Decided in #1016.

## The API token

Scopes used above, and nothing else (each was not tried on its own, so this is a working set, not a proven minimum):

| Scope | For |
|---|---|
| `write:repository` | Creating the mirror, enabling Actions, `mirror-sync` |
| `read:repository` | Listing workflow runs (`GET /repos/<owner>/<repo>/actions/tasks`) |
| `read:user` | `GET /api/v1/user`, to confirm the token |

Forgejo has no Actions scope. Give the token a short expiry, keep it in an environment variable, and never put it in a command line that is logged.

## Checking it

```bash
curl -sS "$FORGEJO_URL/api/v1/version"                                   # the server answers
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/user"   # the token works
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox/branches"   # the refs arrived
curl -sS -H "Authorization: token $FORGEJO_TOKEN" "$FORGEJO_URL/api/v1/repos/<owner>/norvia-sandbox/actions/tasks"   # the runs
```
