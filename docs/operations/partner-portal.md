# Partner portal notes

Notes for engineers and coding agents on the parts of the Partner portal that need more than its [application page](../applications/partner-portal.md). Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

## Fill from an email

A Shipper Persona's booking form has a **Fill from an email** link (`/personas/{persona}/bookings/from-email`). The Persona pastes an email in English or Dutch; the portal sends the text to the Norvia Partner API's `POST /partner/v1/booking-drafts` with the Persona's own token and subscription key, and the booking form opens prefilled with a **Booking draft**. A warning above the form lists the fields the email did not give or gave ambiguously, with the reason. The Shipper edits the form and submits it through the existing path, as the Persona; the draft is not part of the booking, so the booking has no link back to it and its Intake channel stays "Shipper API".

- The text is personal data by nature (a Consignee's name, address and phone). The portal does not log or store it: the log holds the Persona, the outcome and the exception's type on a failure, and the draft lives only in the circuit's `BookingDraftHandoff` until the form takes it.
- "Assistant unavailable" is shown when the call fails or is refused (`Agents:Enabled` off, no `TransportOrders.Draft` role, the rate limit, the model failing or replying something that is not a draft). The form is then filled in by hand. Text with more than one booking, and text over the size limit, get their own message.
- Locally the Partner API stand-in answers with the scripted fake, which knows three synthetic emails (`BookingIntakeSamples` in `Norvia.Agents`): a good English one, a partial Dutch one and one with two bookings. Paste one of them to see the flow; anything else gives an empty draft with everything flagged. Turn the Agents on with `Agents:Enabled` in the AppHost's user secrets.
- Failure behaviour: [failure-behaviour.md](../architecture/failure-behaviour.md); where it runs: [local-vs-azure.md](../architecture/local-vs-azure.md); the role: [identity-and-access.md](../architecture/identity-and-access.md).
