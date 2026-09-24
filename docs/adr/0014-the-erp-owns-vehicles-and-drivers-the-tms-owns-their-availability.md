# The ERP owns Vehicles and Drivers, the TMS owns their availability

ADR 0002 gives every core concept one owner but names none for Norvia's Own fleet. The ERP owns Vehicle and Driver master data: identity, Vehicle type, payload and pallet places, licence categories and the active flag. The TMS holds a replicated read copy, as it does for Carriers. Until the ERP sync exists, both load it from the seed file. The TMS owns everything about availability: Rosters, Shifts, Absences, Off the road days and Driving-hour budgets. Only the TMS knows the Trips that use Vehicles and Drivers up, and availability changes daily as part of planning work, not as master-data maintenance.

## Considered Options

- **The TMS owns Vehicles and Drivers outright:** simplest, but Drivers are employees and Vehicles are fixed assets, both of which the rest of Norvia records outside transport planning. It would also break the pattern set by Shippers and Carriers.
- **The ERP owns availability too:** one owner for everything about the fleet, but every Absence and every Trip would need a round trip before the TMS could plan, and the ERP would have to learn about driving hours.
- **A separate HR or fleet-management system:** realistic for a company of Norvia's size, but it adds an application to the sandbox that no demo needs yet.

## Consequences

The TMS never edits a Vehicle's payload or a Driver's licence. It edits only availability. A Vehicle or Driver made inactive in the ERP drops out of the days that are opened after that, and any Trip already using them is flagged for P11. The ERP never learns about Absences or driving hours. If payroll or HR ever needs them, the TMS would publish them as events. Moving master data to a dedicated HR or fleet system later changes only where the read copy comes from.
