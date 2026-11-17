using System.Security.Cryptography;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// What a Consignee tracks a Transport Order with (ADR 0052): 16 characters of Crockford base32 from a cryptographic random
/// source (80 bits), unrelated to the Transport Order number or any other field. It is a credential, so it never shows up in
/// <see cref="ToString"/>, and so never in a log line or a trace.
/// </summary>
public sealed record TrackingCode
{
    public const int Length = 16;

    /// <summary>How long a code works after the Transport Order's delivery, or after its last progress Tracking event if it is never delivered.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private TrackingCode(string value) => Value = value;

    public string Value { get; }

    public static TrackingCode Generate()
    {
        Span<char> characters = stackalloc char[Length];
        for (var index = 0; index < Length; index++)
        {
            // 32 symbols, so five random bits per character, with no modulo bias.
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new(new string(characters));
    }

    /// <summary>
    /// Reads a code as a person may have typed it: in any case, with Crockford's look-alikes (I and L for 1, O for 0) read
    /// as the digit. Anything else that isn't 16 base32 characters is not a code.
    /// </summary>
    public static bool TryParse(string? text, out TrackingCode? code)
    {
        code = null;
        if (text is not { Length: Length })
        {
            return false;
        }

        Span<char> characters = stackalloc char[Length];
        for (var index = 0; index < Length; index++)
        {
            var character = char.ToUpperInvariant(text[index]);
            character = character switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => character,
            };
            if (!Alphabet.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }

            characters[index] = character;
        }

        code = new(new string(characters));
        return true;
    }

    /// <summary>A code read back from the database, where it was stored in its canonical form.</summary>
    public static TrackingCode FromStored(string value) =>
        TryParse(value, out var code) ? code! : throw new ArgumentException("Not a tracking code.", nameof(value));

    /// <summary>When a code expires, from the instant its Transport Order was delivered, or last progressed.</summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset basis) => basis + Lifetime;

    /// <summary>
    /// When a code expires: 30 days after the Transport Order was delivered, or after its last progress Tracking event if it never is.
    /// A Transport Order with no progress at all counts from when it was received, so a code never outlives an order that never moves.
    /// </summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset? deliveredAt, DateTimeOffset? lastProgressAt, DateTimeOffset receivedAt) =>
        ExpiresAt(deliveredAt ?? lastProgressAt ?? receivedAt);

    /// <summary>Never the code: it is a credential.</summary>
    public override string ToString() => "TrackingCode";
}
