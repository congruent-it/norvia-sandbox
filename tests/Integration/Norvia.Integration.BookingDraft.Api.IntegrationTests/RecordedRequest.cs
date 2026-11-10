namespace Norvia.Integration.BookingDraft.Api.IntegrationTests;

internal sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);
