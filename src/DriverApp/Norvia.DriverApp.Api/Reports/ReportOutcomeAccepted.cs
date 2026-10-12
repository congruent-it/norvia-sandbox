using Norvia.DriverApp.Application.Reports.ReportOutcome;

namespace Norvia.DriverApp.Api.Reports;

/// <summary>The acknowledgement of an outcome: now the report's, or ignored because the report had an outcome already.</summary>
public sealed record ReportOutcomeAccepted(ReportOutcomeResult Result);
