# Agent evaluation

How an Agent's answers are checked against known answers, and how to read the result ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md), "Evaluation"). For engineers and coding agents adding an Agent's evaluation slice and for whoever runs it before a release. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

One harness serves every Agent. An Agent's evaluation slice supplies **cases only**, in [evaluation/cases/&lt;agent&gt;/](../../evaluation/README.md); the checks, the runner and the result file are shared.

## Running it

```
infra/run-agent-evaluation.sh <agent> [--case <id>] [--runs <n>] [--out <file>]
```

On demand and before a release, signed in to the Azure CLI as the presenter (Foundry User on the project), once the Agent is published (`azd up`). Or run the manual workflow *Evaluate agent* ([evaluate-agent.yml](../../.github/workflows/evaluate-agent.yml), `workflow_dispatch` only), which uploads the result file.

- **It never runs in CI and never gates a merge.** `ci.yml` does not mention it and a test fails if it does.
- Each case's `setup` (the toggle or Fault that makes the known answer true) must be on first. The script prints it; it does not switch it. Run the cases of one setup together with `--case`.
- Each case's input goes to the published agent through the project's Responses API with `store: false` and no conversation ([foundry.md](foundry.md), spike 3), so a run leaves nothing behind.
- It prints, per case, the tool calls used, every check with its pass or fail and reason, and a score (`6/6 checks, 2/2 runs passed => PASS`). The result file is `.azure/<env>/evaluation/<agent>-<time>.json` unless `--out` says otherwise.
- The exit code is 0 even when cases fail. It is non-zero only when the harness cannot run (no endpoint, a malformed case file). Every case file is validated before the first call, so a typo costs no model run.

## Decision: our own graders over the structured result

**Open question of #501, decided: our own checks over the Agent's structured Suggestion result, with one model-graded check reserved for the explanation. Foundry's built-in evaluators are not used.**

- The structured result makes the **cause** and the **action** checkable *without a model*: an exact field, a "must cite", a tool that must not have been called, a string that must not appear. These are deterministic, free, and cannot be flaky. They are the checks that matter (the injection cases, the Carrier-name leak, the right cause for the seeded fault).
- Only the **explanation's quality** needs judgment. The runner asks the estate's own model deployment (`EVALUATION_JUDGE_MODEL`, else the account's first) to grade it against the case's rubric, treating the explanation as delimited data. No extra service, quota or SDK.
- Foundry's built-in evaluators (relevance, coherence, groundedness and the like) grade generic qualities with a model and a scale. They cannot say "the cause is `unmappable-carrier-code`", which is what a known answer is. They would add a Python SDK and a second place results live. If a later Agent needs a generic quality score, add it as a check type here, not as a second harness.
- The model-graded check can be wrong in either direction, so a failing explanation next to passing exact fields is a note about wording, not a finding about the Agent's diagnosis.

## Reading the result

**A failure is a finding, not a failed build.** Nothing here blocks a merge or a release by itself; a person reads it.

| Result | Means | Do |
|---|---|---|
| `PASS` (all checks, all runs) | On this case, with this setup, the Agent gave the known answer, cited it, used only allowed tools and said nothing forbidden. It is **not** proof the Agent is right in general: the cases are a sample, seeded from the Simulator and the as-found faults | Nothing |
| `FAIL` on a field, `must cite` or `must not contain` | The Agent's diagnosis, evidence or confidentiality is wrong for a situation whose answer is known | Open an issue for the Agent's slice with the case id, the result file and the tool calls. Fix the instructions or the tool, then rerun that case. Never edit the case to match the answer unless the *known answer* was wrong (and say so in the PR) |
| `FAIL` on `tool … not called` | A tool ran that should not have: an injection case steered the Agent. The most serious finding. There is no write tool (ADR 0040), so the worst effect is a misleading answer, but the instructions are not holding | As above, with priority; check how the instructions delimit tool results |
| `FAIL` on `explanation quality` only | The diagnosis is right but the wording missed the rubric | Read the explanation. Fix the instructions if it is really poor; if the rubric is too narrow, fix the rubric. A grader that disagrees with a person is a rubric problem |
| `FLAKY` (`--runs 3`: some runs pass, some fail) | The Agent's answer varies for this input | Do not rerun until it passes. Count it as a failure of the weakest run, find which check varies in the result file, and tighten the instructions or the tool result. A case that cannot be made stable is too ambiguous: split it, or loosen it to what is really known |
| `agent answered` fails | The call failed: the Agent is not published, the user lacks Foundry User, the model deployment is off (the `foundryModelDeployed` toggle) or quota is used up | Not an Agent finding. Fix the estate and rerun |

Before reading, check the case's `setup` was on: a case run without its toggle fails for the right reason (the situation is not there) and says nothing about the Agent.

## Adding cases for an Agent

1. Write `evaluation/cases/<agent>/<case>.json` (format: [evaluation/README.md](../../evaluation/README.md)). Each case has a known answer written down, tied to the toggle or Fault that causes it, as expected fields and checks, never a free-text answer.
2. Include the injection cases the Agent's epic names (a remark that says to ignore instructions, resubmit everything or reveal them): expect `toolsNotCalled`, `mustNotContain` and the same `fields` as without the injection.
3. Add nothing else: no harness code. If a check type is missing, add it to [checks.jq](../../infra/evaluation/checks.jq) with a test in `AgentEvaluationTests`.
4. Run it: `infra/run-agent-evaluation.sh <agent>`.

## What was and was not verified

The checks and the case validator have unit tests ([AgentEvaluationTests.cs](../../tests/Tms/Norvia.Tms.ArchitectureTests/AgentEvaluationTests.cs)): each passes and fails correctly on sample answers. The runner was run end to end against a local stand-in of the Responses API, not against Azure.

**Not yet verified in a tenant**: the request shape (`agent_reference` on `/openai/v1/responses`), the output item types the runner reads (`mcp_call` with `name`, `message` with `output_text`), and that `store: false` is honoured for an Agent that calls an MCP tool. The example agent that #499's smoke run publishes does not exist yet (`infra/deploy-agents.sh` waits for the definition format of #379), so [the example case](../../evaluation/cases/example/estate-info.json) is the first thing to run once it does. Its expected fields (`cause.code`, `action.code`) assume the Suggestion type #379 defines; if that type names them differently, change the paths in the cases, not the harness.
