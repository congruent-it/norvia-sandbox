# CI runs on the homelab's Forgejo on push and reports to GitHub as commit statuses

**Status: Proposed.** The gates of a PR (build, format, tests, coverage, link check) are moving off GitHub Actions to the homelab's Forgejo, which has more capacity for the Aspire integration and UI legs ([#1030](https://github.com/congruent-it/norvia-sandbox/issues/1030)). This is the pattern every gate copies (#1039). The Forgejo setup is in [forgejo.md](../operations/forgejo.md).

## Decision

- **Forgejo pull-mirrors GitHub and runs the gates.** GitHub stays the source of truth for code, issues, PRs and reviews. Forgejo has a read-only copy of the branches and is never pushed to. It is not reachable from the Internet, so every call between the two is outbound from the homelab.
- **Workflows trigger on `push`, not `pull_request`.** The mirror has no PRs, so `pull_request` never fires. A sync starts the `push` workflows of a new branch and of new commits on a mirrored one. Workflows live in `.forgejo/workflows/` and are committed on GitHub.
- **Results go back as commit statuses.** The composite action `.forgejo/actions/report-status` posts `pending` when a job starts and `success`, `failure` or `error` when it ends, to `POST /repos/congruent-it/norvia-sandbox/statuses/{sha}`, with the context `forgejo/<job>` and a `target_url` at the Forgejo run. The token is the fine-grained token of the Forgejo secret `GH_STATUS_TOKEN` (Commit statuses write on this repository only). GitHub shows the statuses on the commit and on a PR from the branch.
- **A gate that cannot report is not green.** The call is not retried silently or ignored: a failed call fails the job.
- **A newer push cancels the older run** (`concurrency: ci-${{ github.ref }}`, `cancel-in-progress`). The last step of a job runs `if: always()` and reports `job.status`; a `cancelled` is posted as `error` with "superseded". The status of the older commit is not shown on the PR, which shows the head commit, so a `pending` the runner failed to replace (a run killed before its last step) is harmless to the PR and stays on that old commit only.
- **`evaluate-agent.yml` stays on GitHub.** It is manual (`workflow_dispatch`), needs the presenter's Azure identity and never gates a merge ([ADR 0040](0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md), [agent-evaluation.md](../operations/agent-evaluation.md)).
- **The script is PowerShell 7** with a Pester test ([ADR 0048](0048-scripts-are-powershell-7-not-bash.md)).

## Considered Options

- **Keep the gates on GitHub Actions:** nothing to run, but hosted runners are slow and costly for about 50 Aspire legs, each starting SQL Server and the Service Bus emulator in Docker.
- **A self-hosted GitHub runner on the homelab:** no mirror and no statuses to post. But the runner registers with GitHub and takes jobs from the Internet-facing side of GitHub's service, and PR code from a fork could run on the homelab.
- **A GitHub webhook to Forgejo for an instant start:** needs Forgejo reachable from the Internet, which is ruled out.

## Consequences

- **Fork PRs are not tested.** A fork's branch is not in the mirror. The sandbox has no fork contributors; if it gets some, a maintainer pushes the commit to a branch of the repository.
- **A PR is not tested as the merge commit.** `push` tests the branch head, not the result of merging it into `main`. A branch behind `main` can be green and break `main`. Keep the branches current (rebase, or merge `main`), or add the merge as a gate later.
- **The mirror interval is a latency.** A commit arrives within 0 to 10 minutes (the default interval), then the run starts. The Sync now button, or the `mirror-sync` API, skips the wait.
- **A PR shows a status only for a commit Forgejo has built.** Until the sync, the PR shows nothing for the new head. It is not blocked, because branch protection is not changed here.
- **The statuses are not branch protection.** Which `forgejo/*` contexts are required is a later issue.
- **A stuck `pending`** can remain on a superseded commit if the runner is killed before the closing step. A newer push replaces the head, and the PR shows that.
- **The status token expires** (see forgejo.md) and a job fails loudly when it does.
