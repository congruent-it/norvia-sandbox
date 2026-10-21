namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>Where the dead-letter pages link to.</summary>
public static class DeadLetterLinks
{
    /// <summary>The messages of a queue, or of a subscription named <c>topic/subscription</c>.</summary>
    public static string Messages(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return "/dead-letter-queues/" + string.Join('/', entity.Split('/').Select(Uri.EscapeDataString));
    }
}
