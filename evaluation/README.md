# Agent evaluation cases

The known answers the evaluation harness ([infra/run-agent-evaluation.sh](../infra/run-agent-evaluation.sh)) checks an Agent against. One directory per agent, one file per case: `evaluation/cases/<agent>/<case>.json`. A new Agent's evaluation slice adds files here and nothing else: the checks, the runner and the result file are shared. How to run it and read the result: [docs/operations/agent-evaluation.md](../docs/operations/agent-evaluation.md).

```json
{
  "id": "p5-unmappable-code",
  "agent": "integration-operations",
  "description": "Why this case exists and where its known answer comes from.",
  "setup": { "fault": "the Fault or toggle that must be on, and how it is seeded" },
  "input": "The question about the seeded situation, or the text the Agent is given.",
  "expect": {
    "fields": { "cause.code": "unmappable-carrier-code", "action.code": ["contact-carrier", "map-code"] },
    "mustCite": ["get_message_trail"],
    "mustNotContain": ["Carrier name", "a Spot remark"],
    "toolsNotCalled": ["get_run"],
    "explanation": { "rubric": "What a good explanation says.", "field": "explanation" }
  }
}
```

| Field | Meaning |
|---|---|
| `id` | Lower-case letters, digits and hyphens; unique in the Agent's directory |
| `agent` | The published agent's name, the same as the directory |
| `setup` | The toggle or Fault that must be on (and the seed) for the known answer to hold. The harness prints it, the presenter switches it |
| `input` | A string: the question, or the text for an Agent that reads text |
| `expect` | At least one check. All must pass for the case to pass |

Checks, each with a pass or fail and a reason (the logic is [infra/evaluation/checks.jq](../infra/evaluation/checks.jq)):

| Check | Passes when |
|---|---|
| `fields` | Each dotted path of the structured Suggestion result has exactly the value. An array of values means any one of them. No model involved |
| `mustCite` | Each string is in the `source` of at least one `evidence` item (a tool name or an id) |
| `mustNotContain` | None of the strings is anywhere in the answer, whatever the case |
| `toolsNotCalled` | The Agent made no call to any of these tools |
| `explanation` | A model grader says the explanation meets the `rubric`. The only model-graded check; `field` is where the explanation is in the result (default `explanation`) |

The structured result is read by path (`cause.code`, `action.code`, `evidence[].source`, `explanation`), so a change in the Suggestion type's shape changes the cases' paths, not the harness.
