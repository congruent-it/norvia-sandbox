# The Shipper tracking map is Liquid, with the clock rules written out

The map from the TMS's Canonical Model answer to the Shipper tracking format is a pair of Liquid templates, `infra/estate/partner-api/shipper-tracking.liquid` (one Transport Order) and `shipper-tracking-list.liquid` (a list), run by API Management's `set-body` policy. P8 is a read path, and Logic Apps are never used for read actions (ADR 0003), so the map has to live in the policy, and the policy's own template language is Liquid. Unlike the intake map (ADR 0019) it has nowhere to run .NET.

The hard part is the same, the other way round: the Shipper format has local date/times, and the Canonical Model has UTC instants. Liquid has no time zone support, so the templates carry the clock rules themselves: the countries of Europe, each on its mainland's clock, with the European Union's rule (the clocks go forward at 01:00 UTC on the last Sunday of March and back at 01:00 UTC on the last Sunday of October) and the countries that keep one clock all year. The weekday is worked out with integer arithmetic, because `set-body` Liquid runs in C# mode and the `date` filter would convert the instant to the gateway's clock. The conversion is written out in each template, because `set-body` has no include.

Tests run the templates in CI with DotLiquid 2.0.361, the version API Management uses, in C# mode. They compare every country's conversion with the time zone rules of the machine, around the days the clocks change and the ends of months and years, so a wrong rule or a missing country fails the build.

## Considered Options

- **A Logic App or Function behind the policy, so the map can be .NET:** it makes the time zones easy, but puts a hop on a read path that ADR 0003 keeps free of Logic Apps, and gives the Shipper a slower, more fragile query.
- **The TMS sends local times:** the TMS would then know the Shipper's format. The Canonical Model stays one format for everyone, and the policy is where the formats differ (P8).
- **An XML policy expression (C#) with `TimeZoneInfo`:** it would work in API Management, but a policy expression can't be tested in CI without Azure, and the issue and P8 ask for a Liquid map.

## Consequences

- Mainland clocks only. A place in the Azores or the Canary Islands gets the clocks of its country's mainland: the Canonical Model's place has a city and a country, no postcode. The intake map (ADR 0019) does use postcodes for them.
- A country the templates don't know is on UTC, and a country with no value takes the delivery address's clocks for an event. Both show up as wrong times in review, not as errors.
- A rule change (a country dropping daylight saving time, as the EU has debated) is a template change in both files, caught by the clock test the day the machine's time zone data has it.
- A Tracking status or event type the Shipper format has no code for gives an empty code, which the Shipper tracking schemas refuse. The policy that runs the map (P8.6) is where that is caught.
- The templates rely on `parse-date="false"` on `set-body`, so API Management hands over the date strings as text.
- The ETA and the last update are on the delivery address's clocks in both the list and the detail, because a list item has no place for its last update.
