using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Guild.Application;

/// <summary>Japanese presentation copy shared by the application and the Godot client.</summary>
public static class DisplayText
{
    private const string ResourcePrefix = "Guild.Application.DisplayText.ja.";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Catalog = new(LoadCatalog);

    /// <summary>Read-only copy catalog. Text never participates in game calculations.</summary>
    public static IReadOnlyDictionary<string, string> Entries => Catalog.Value;

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Entries.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Display text key was not found: {key}");
    }

    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, Get(key), args);

    private static IReadOnlyDictionary<string, string> LoadCatalog()
    {
        var assembly = typeof(DisplayText).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (resources.Length == 0)
            throw new InvalidDataException("No embedded Japanese display text catalog was found.");

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException($"Display text resource could not be opened: {resource}");
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Display text catalog must be a JSON object: {resource}");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"Display text entries must have nonempty keys and string values: {resource}");
                if (!entries.TryAdd(property.Name, property.Value.GetString()!))
                    throw new InvalidDataException($"Duplicate display text key: {property.Name}");
            }
        }
        return new ReadOnlyDictionary<string, string>(entries);
    }
}
