using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Guild.Domain.Numerics;

namespace Guild.Domain.Serialization;

public static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new BigIntegerJsonConverter());
        options.Converters.Add(new RationalJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("BigInteger must be a decimal string.");
        if (!BigInteger.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("Invalid BigInteger decimal string.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A rational must contain numerator and denominator strings.");
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("numerator", out var n) || !root.TryGetProperty("denominator", out var d)
            || n.ValueKind != JsonValueKind.String || d.ValueKind != JsonValueKind.String)
            throw new JsonException("A rational must contain numerator and denominator strings.");
        if (!BigInteger.TryParse(n.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator)
            || !BigInteger.TryParse(d.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator)
            || denominator.IsZero) throw new JsonException("Invalid rational.");
        return new Rational(numerator, denominator);
    }
    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("numerator", value.Numerator.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("denominator", value.Denominator.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }
}
