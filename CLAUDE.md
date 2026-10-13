# Norvia sandbox

## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues for congruent-it/norvia-sandbox, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`. Integration tests and UI tests are written in issues of their own, separate from the feature issue (see "Splitting work into issues" there).

### Triage labels

The five default triage labels (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: a root `GLOSSARY.md` and `docs/adr/`. See `docs/agents/domain.md`.

## Coding guidelines

All .NET code and tests follow `docs/coding-guidelines.md`: Clean Architecture (dependencies point inwards, enforced by architecture tests), Ardalis repositories and specifications for data access, Blazor with Fluent UI components for web apps, xUnit + NSubstitute for unit tests (at least 80% line coverage), and Playwright for UI tests. The tracking contract in §7 also applies to APIM policies, Logic Apps and Service Bus subscriptions.
