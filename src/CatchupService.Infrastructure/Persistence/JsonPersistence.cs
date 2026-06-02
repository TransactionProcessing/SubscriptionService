using System.Text.Json;

namespace CatchupService.Infrastructure.Persistence;

internal static class JsonPersistence
{
    public static string Serialize(IReadOnlyDictionary<string, string> value) =>
        JsonSerializer.Serialize(value);

    public static Dictionary<string, string> DeserializeDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
