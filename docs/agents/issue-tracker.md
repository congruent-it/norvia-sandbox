# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

## Splitting work into issues

A feature issue covers the code and its unit and architecture tests (80% line coverage, `docs/coding-guidelines.md` §10). Integration tests and UI tests are never part of it. Each gets an issue of its own, so a slow or flaky suite never blocks a feature and each can be picked up by itself:

- **Integration tests issue**: `<Feature title>: integration tests`. The API, adapter and AppHost-level cases (Testcontainers, `Aspire.Hosting.Testing`, the Service Bus emulator, supporting maps and Partner portal boundaries).
- **UI tests issue**: `<Feature title>: UI tests`. The Playwright journeys.

Both are blocked by the feature issue. Open them when the feature issue is written, so the cases are chosen while the design is fresh, and move the feature's `Testing Decisions` cases to the issue they belong to. Post-deploy smoke runs stay with the issue that deploys. Create them with `gh issue create`, make each a GitHub sub-issue of the feature issue (`gh api --method POST repos/<owner>/<repo>/issues/<feature>/sub_issues -F sub_issue_id=<database id>`) with a native `blocked_by` dependency on it (see Wayfinding operations), begin the body with `Blocked by #<feature>`, and label them `needs-triage` or `ready-for-agent` like any issue. In the feature issue, replace the test criteria with `Integration tests: #<n>` and `UI tests: #<n>`.

## Unblocking issues when you finish one

When an issue is done (its PR is merged and you close it), unblock the issues that were waiting on it, in the same step:

1. **Find the dependents.** Native dependencies: `gh api repos/<owner>/<repo>/issues/<n>/dependencies/blocking --jq '.[] | {number, title, state}'`. Body lines: `gh issue list --state open --search '"Blocked by #<n>" in:body' --json number,title,body`. Check both; older issues use only the line.
2. **Clear the edge.** Remove `#<n>` from the dependent's `Blocked by:` line, and delete the line when no blocker is left (`gh issue edit <dep> --body ...`). GitHub's native `blocked_by` count already ignores closed blockers, so that edge needs no change; remove it only if it is stale (`gh api --method DELETE repos/<owner>/<repo>/issues/<dep>/dependencies/blocked_by/<blocker-db-id>`).
3. **Check what is left.** If the dependent still has an open blocker, leave it and say which. If none is left, it is unblocked: confirm it still carries its triage label (`ready-for-agent` or `ready-for-human`), and add it if the issue was written as waiting for this one.
4. **Say so on the dependent.** `gh issue comment <dep> --body "Unblocked: #<n> is done."`, with the PR link.

Close the finished issue first, then unblock, and list the issues you unblocked in your final message. Do not start the unblocked issue unless asked.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --comments` and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies**, the canonical, UI-visible representation. Add an edge with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`, _not_ the `#number` or `node_id`). GitHub reports `issue_dependencies_summary.blocked_by` (open blockers only, the live gate). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`, the session's first write.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.
