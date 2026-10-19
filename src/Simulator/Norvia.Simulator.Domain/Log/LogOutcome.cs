namespace Norvia.Simulator.Domain.Log;

public enum LogOutcome
{
    /// <summary>A line of the Simulator itself: the run started, was cancelled or finished.</summary>
    Info,

    Accepted,
    AlreadyBooked,
    Rejected,

    /// <summary>Norvia refused the Persona's credentials.</summary>
    Refused,

    /// <summary>Nothing reached Norvia, or its answer couldn't be read.</summary>
    NotReceived,

    /// <summary>The Planner assigned a Transport Order, which planned its Shipment.</summary>
    Planned,

    /// <summary>The Planner reassigned a Planned Shipment.</summary>
    Reassigned,

    /// <summary>The TMS refused the Planner's assignment or reassignment, with a code that says which rule it broke.</summary>
    PlanningRefused,

    /// <summary>The Planner's call didn't reach the TMS, or its answer couldn't be read.</summary>
    PlanningNotReceived,

    /// <summary>The Planner released a Planned Shipment, handing it to its Carrier, which created its Carrier booking.</summary>
    Released,

    /// <summary>The Planner cancelled the Confirmed Carrier booking of the Shipment of an open Exception.</summary>
    BookingCancelled,

    /// <summary>The Planner resolved an Exception.</summary>
    ExceptionResolved,

    /// <summary>The Planner found the Exception a tracking event opened.</summary>
    ExceptionFound,

    /// <summary>A Carrier confirmed its Carrier booking.</summary>
    Confirmed,

    /// <summary>A Carrier declined its Carrier booking.</summary>
    Declined,

    /// <summary>A Carrier was asked and gave no answer: its Carrier booking stays Requested.</summary>
    NoAnswer,

    /// <summary>Norvia accepted a Carrier's tracking message.</summary>
    TrackingSent,

    /// <summary>Norvia accepted a Carrier's Proof of delivery.</summary>
    ProofOfDeliverySent,

    /// <summary>Norvia refused a Carrier's answer, tracking message or Proof of delivery, with a code that says which rule it broke.</summary>
    CarrierRefused,

    /// <summary>A Carrier's call didn't reach Norvia, or its answer couldn't be read.</summary>
    CarrierNotReceived,

    /// <summary>The Planner created a Draft Trip.</summary>
    TripBuilt,

    /// <summary>The Planner dispatched a Trip to its Driver.</summary>
    TripDispatched,

    /// <summary>The Driver app took a Driver's report; the TMS records it later and the Driver app tells the Driver what became of it.</summary>
    DriverReported,

    /// <summary>The Driver app refused a Driver's report, with a code that says which rule it broke.</summary>
    DriverRefused,

    /// <summary>A Driver's call didn't reach the Driver app, or its answer couldn't be read.</summary>
    DriverNotReceived,

    /// <summary>The TMS recorded the Driver's End Trip: the Trip is Completed.</summary>
    TripCompleted,

    /// <summary>Finance ran Invoice now in the ERP and it issued a Shipper invoice.</summary>
    InvoiceIssued,

    /// <summary>Finance ran Invoice now and the Shipper had no Waiting lines, so the ERP issued nothing.</summary>
    NothingToInvoice,

    /// <summary>Finance issued a Credit note on a line of a Shipper invoice in the ERP.</summary>
    CreditNoteIssued,

    /// <summary>The ERP refused Invoice now or a Credit note, with a code that says which rule it broke.</summary>
    InvoicingRefused,

    /// <summary>Finance's call didn't reach the ERP, or its answer couldn't be read.</summary>
    InvoicingNotReceived,

    /// <summary>An invoice or Credit note arrived in the Shipper Persona's invoices inbox.</summary>
    InboxReceived,
}
