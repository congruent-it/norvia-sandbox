# Partner portal notes

Notes for engineers and coding agents on the parts of the Partner portal that need more than its [application page](../applications/partner-portal.md). Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

## Fill from an email

A Shipper Persona's booking form has a **Fill from an email** link (`/personas/{persona}/bookings/from-email`). The Persona pastes an email in English or Dutch; the portal sends the text to the Norvia Partner API's `POST /partner/v1/booking-drafts` with the Persona's own token and subscription key, and the booking form opens prefilled with a **Booking draft**. A warning above the form lists the fields the email did not give or gave ambiguously, with the reason. The Shipper edits the form and submits it through the existing path, as the Persona; the draft is not part of the booking, so the booking has no link back to it and its Intake channel stays "Shipper API".

- **The review screen.** The form is the review: it shows the Shipper where each value came from and what the assistant was unsure of.
  - **Quote per field.** Under every prefilled field that has a quote the form shows "From the email" and the words the value came from (the draft's `quotes`).
  - **Flags.** A field the email did not give is empty and flagged "Missing"; a field that has a value the assistant was not sure of is flagged "Check this". Both carry the assistant's reason (the draft's `missing`, such as two weights in the email). The Intake agent marks a field it could not settle (two weights, "about a ton") as `ambiguous` in the draft's `missing`; the portal also flags a value it cannot read (a weight without a unit) as "Check this". Any other flagged field is "Missing".
  - **Submit gating.** The booking is not sent until each flagged field is handled: confirmed with the button under it, or edited (including filled in, or its goods line removed). `DraftReview` holds this, and the form checks it on submit, after the validator, which still decides what is a valid booking. An edit counts for as long as the field differs from the draft; putting the draft's value back unhandles it, while a confirmation stays. Both the flags and the validator's errors show on one submit. A flag on the whole booking ("the text describes no booking") has nothing to edit and needs a confirmation.
  - **What was ignored.** "Text the assistant did not use" lists the pieces of the email the assistant left out, such as a signature or a disclaimer (the draft's `ignored`). The Intake agent library keeps only the pieces that are in the text.
  - **Start again.** "Start again with other text" goes back to the paste screen. The draft, its quotes and its confirmations live only in the circuit and are gone once the form is replaced.
- The text is personal data by nature (a Consignee's name, address and phone). The portal does not log or store it: the log holds the Persona, the outcome and the exception's type on a failure, and the draft lives only in the circuit's `BookingDraftHandoff` until the form takes it.
- "Assistant unavailable" is shown when the call fails or is refused (`Agents:Enabled` off, no `TransportOrders.Draft` role, the rate limit, the model failing or replying something that is not a draft). The form is then filled in by hand. Text with more than one booking, and text over the size limit, get their own message.
- Locally the Partner API stand-in answers with the scripted fake, which knows three synthetic emails (`BookingIntakeSamples` in `Norvia.Agents`): a good English one, a partial Dutch one and one with two bookings. Paste one of them to see the flow; anything else gives an empty draft with everything flagged. Turn the Agents on with `Agents:Enabled` in the AppHost's user secrets.
- Failure behaviour: [failure-behaviour.md](../architecture/failure-behaviour.md); where it runs: [local-vs-azure.md](../architecture/local-vs-azure.md); the role: [identity-and-access.md](../architecture/identity-and-access.md).

## Tracking assistant

The panel on the tracking screen ([P8](../processes/tms/p8-shipper-visibility.md#ask-about-your-orders-the-shipper-tracking-assistant), [ADR 0049](../adr/0049-the-shipper-tracking-assistant-has-no-tools-and-only-restates-what-the-portal-fetched-with-the-personas-token.md)) shows only while `Agents:Enabled` is on.

- **Turn it on in Azure:** `azd env set ESTATE_AGENTS_ENABLED_SETTING true`, then `azd up`. `infra/deploy-agents.ps1` (a postdeploy hook) publishes the Agent into the `norvia-intake` project with no role. Without a model answer the panel says "Assistant unavailable" and the screen works as before.
- **Limits** are estate parameters, set before `azd up` with `ESTATE_ASSISTANT_<NAME>_SETTING`: `QUESTIONS_PER_MINUTE` (6), `QUESTIONS_PER_HOUR` (60), `MAX_QUESTION_CHARACTERS` (500), `MAX_ORDERS` (20), `MAX_EVENTS_PER_ORDER` (20), `MAX_OUTPUT_TOKENS` (400), `DAILY_TOKENS_PER_PERSONA` (50000) and `DAILY_TOKENS_ESTATE` (500000). The counters are in the portal's memory, so it runs one replica and a restart resets them.
- **Locally** the scripted fake plays the Agent; there is nothing to configure.
- **Post-deploy smoke run:** [infra/smoke-test-tracking-assistant.ps1](../../infra/smoke-test-tracking-assistant.ps1) (a presenter asks as two Shipper Personas; the script checks the Agent's identity holds no role, the answer's values are in the data, the other Shipper's order is not reachable and no question is in the log).
- **What the model costs** is tagged `workload: foundry-agents`, `cost-category: ai-model` on the Foundry resources, as for every Agent.
