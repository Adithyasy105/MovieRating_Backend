using System.Text.Json;
using MongoDB.Bson;

namespace MovieRating.Pagination;

public static class ReviewCursorCodec
{
    public static string Encode(ReviewCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        Validate(cursor);

        var json = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(json)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static ReviewCursor Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A cursor value is required.",nameof(value));
        }

        // Prevent excessively large cursor values.
        if (value.Length > 2048)
        {
            throw new ArgumentException("The cursor is invalid.",nameof(value));
        }

        try
        {
            var base64 = value
                .Replace('-', '+')
                .Replace('_', '/');

            base64 += new string('=',(4 - base64.Length % 4) % 4); // Pad with '=' to make the length a multiple of 4.

            var bytes = Convert.FromBase64String(base64);

            var cursor =JsonSerializer.Deserialize<ReviewCursor>(bytes);

            if (cursor is null)
            {
                throw new ArgumentException("The cursor is invalid.");
            }

            Validate(cursor);

            return cursor;
        }
        catch (FormatException)
        {
            throw new ArgumentException("The cursor is invalid.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("The cursor is invalid.");
        }
    }

    private static void Validate(ReviewCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (cursor.CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The cursor timestamp must be UTC.");
        }

        if (!ObjectId.TryParse(cursor.Id, out _))
        {
            throw new ArgumentException("The cursor contains an invalid review ID.");
        }
    }
}