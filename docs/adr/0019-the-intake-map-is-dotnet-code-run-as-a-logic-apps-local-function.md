# The intake map is .NET code, run as a Logic Apps local function

The map from the partner booking format to the Canonical Model `TransportOrder` is a small .NET 8 class library, `Norvia.Integration.TransportOrderIntake`. The intake Logic App (Logic Apps Standard) runs it with the built-in action "Call a local function in this logic app", so the run history still shows the partner message going in and the Canonical Model message coming out. The map's hard part is the partner's local date/times: turning them into UTC needs the time zone rules of the address's country, including daylight saving time, and Liquid (DotLiquid in Logic Apps) has no time zone support. In .NET, `TimeZoneInfo` does this correctly. The same map runs unchanged in xUnit against the sample messages in `contracts/partner-api/samples`, so CI tests it without Azure.

The map never rejects a booking. A value it can't read (an unknown country, a date in another format, a weight without a unit) is left out of the Canonical Model message, and the TMS's Transport Order rules report what is then missing or invalid. The Shipper hears every reason from one place, with the TMS's stable codes.

## Considered Options

- **A Liquid template ("Transform JSON to JSON"):** the idiomatic Logic Apps map, testable in CI with the DotLiquid version Logic Apps uses. But converting local time to UTC would mean hand-coding the EU daylight saving rules in Liquid, only for the time zones written into the template, and parsing numbers with units and decimal commas is fragile there.
- **The partner sends UTC offsets with its times:** Liquid would then only have to normalise to UTC. But the partner format would no longer have local date/times, which is part of why the Logic App does real work worth showing.
- **Data Mapper (XSLT 3.0):** XPath has no named time zones either, and running the map outside Azure needs Saxon.

## Consequences

- The map project targets `net8.0`, the only .NET version Logic Apps local functions run on, while the rest of the solution targets .NET 10. The tests run it on .NET 10.
- The map reads IANA time zone IDs (`Europe/Amsterdam`). On Windows, which Logic Apps Standard runs on, .NET finds them through ICU, so the Logic App must not run in invariant globalization mode.
- The function wrapper, `Norvia.Integration.TransportOrderIntake.Functions`, came with the intake Logic App (#25). Building it writes the functions into the Logic App project's `lib/custom` folder. The map itself has no dependency on the Logic Apps SDK.
- The relay of the TMS's outcome to the partner format is .NET code in the map's library too, run as a local function, so it can be tested in CI as well. Logic Apps' own test SDK runs workflows only on Windows, so the workflow is kept thin and CI checks its definition, not its runs.
- Changing the map is a code change that needs a build and deployment of the Logic App, not a map upload.
