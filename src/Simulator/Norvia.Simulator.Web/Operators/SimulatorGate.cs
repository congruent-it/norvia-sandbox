namespace Norvia.Simulator.Web.Operators;

/// <summary>
/// The Simulator feeds the estate with orders, so it does nothing unless the estate turns it on (<c>Simulator:Enabled</c>).
/// Switched off, it answers every request but its health checks with 404 and a sentence saying why.
/// </summary>
public sealed class SimulatorGate(RequestDelegate next, bool enabled)
{
    public const string OffText = "The Simulator is switched off. Set Simulator:Enabled to turn it on.";

    private static readonly string[] OpenPaths = ["/health", "/alive"];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (enabled || OpenPaths.Any(path => context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync(OffText);
    }
}
