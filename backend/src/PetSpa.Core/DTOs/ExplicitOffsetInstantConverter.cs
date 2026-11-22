using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs;

public sealed class ExplicitOffsetInstantConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Thời điểm phải là chuỗi ISO 8601 có offset.");
        var text = reader.GetString()!;
        var hasOffset = text.EndsWith('Z') || text.Length >= 6 &&
            (text[^6] == '+' || text[^6] == '-') && text[^3] == ':' &&
            char.IsAsciiDigit(text[^5]) && char.IsAsciiDigit(text[^4]) &&
            char.IsAsciiDigit(text[^2]) && char.IsAsciiDigit(text[^1]);
        if (!text.Contains('T') || !hasOffset || !reader.TryGetDateTimeOffset(out var instant))
            throw new JsonException("Thời điểm phải có Z hoặc offset ±HH:mm.");
        return instant;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
