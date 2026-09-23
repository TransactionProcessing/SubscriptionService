using System.Globalization;
using System.Text.Json;

namespace SubscriptionService.Worker.Ui;

public sealed record EventStoreIndexListItemViewModel(
    string Name,
    string? State,
    string Summary,
    string RawJson,
    bool IsBuiltIn = false,
    string Type = "User-defined")
{
    public string StateOrDefault => string.IsNullOrWhiteSpace(State) ? "Unknown" : State;

    public bool IsReadOnly => IsBuiltIn;
}

public static class EventStoreIndexPresentation
{
    public static EventStoreIndexListItemViewModel CreateBuiltInIndex(string name, string type) =>
        new(name, "Built-in", $"KurrentDB {type} secondary index", string.Empty, true, type);

    public static IReadOnlyList<EventStoreIndexListItemViewModel> CombineIndexLists(
        IEnumerable<EventStoreIndexListItemViewModel> userIndexes,
        IEnumerable<EventStoreIndexListItemViewModel> builtInIndexes) =>
        userIndexes
            .Concat(builtInIndexes)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string GetStateBadgeClass(string? state) => (state ?? string.Empty).ToLowerInvariant() switch
    {
        "index_state_started" => "badge badge-success",
        "index_state_stopped" => "badge badge-muted",
        "index_state_building" => "badge badge-warning",
        "index_state_failed" => "badge badge-danger",
        _ => "badge badge-info"
    };

    public static string GetStateIcon(string? state) => (state ?? string.Empty).ToUpperInvariant() switch
    {
        "INDEX_STATE_STARTED" => "▶",
        "INDEX_STATE_STOPPED" => "⏸",
        "INDEX_STATE_BUILDING" => "⚙",
        "INDEX_STATE_FAILED" => "✕",
        _ => "•"
    };

    public static IReadOnlyList<EventStoreIndexListItemViewModel> ParseIndexList(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Array.Empty<EventStoreIndexListItemViewModel>();
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return ParseIndexList(document.RootElement);
        }
        catch (JsonException)
        {
            return Array.Empty<EventStoreIndexListItemViewModel>();
        }
    }

    public static string FormatJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return value;
        }
    }

    public static string GetResponseSummary(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No response body";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return $"{root.GetArrayLength()} index{(root.GetArrayLength() == 1 ? string.Empty : "es")} returned";
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(root, "name", out var name))
                {
                    return name!;
                }

                if (TryGetString(root, "state", out var state))
                {
                    return state!;
                }
            }
        }
        catch (JsonException)
        {
        }

        return "Response received";
    }

    public static string? GetPrimaryState(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    var state = GetStatus(element);
                    if (!string.IsNullOrWhiteSpace(state))
                    {
                        return state;
                    }
                }
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var state = GetStatus(root);
                if (!string.IsNullOrWhiteSpace(state))
                {
                    return state;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static string BuildSummary(JsonElement element)
    {
        var bits = new List<string>();

        foreach (var propertyName in new[] { "state", "status", "enabled", "stream_prefix", "projection_type", "mode" })
        {
            if (TryGetProperty(element, propertyName, out var property) && TryDescribe(property, out var value))
            {
                bits.Add($"{ToDisplayName(propertyName)}: {value}");
            }
        }

        return bits.Count == 0 ? "No additional details" : string.Join(" · ", bits);
    }

    public static string GetDisplayName(JsonElement element, int fallbackIndex)
    {
        foreach (var propertyName in new[] { "name", "index_name", "indexName", "id", "key" })
        {
            if (TryGetString(element, propertyName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return $"Index {fallbackIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string? GetStatus(JsonElement element)
    {
        foreach (var propertyName in new[] { "state", "status", "enabled" })
        {
            if (TryGetProperty(element, propertyName, out var property) && TryDescribe(property, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<EventStoreIndexListItemViewModel> ParseIndexList(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var items = new List<EventStoreIndexListItemViewModel>(root.GetArrayLength());
            var index = 0;

            foreach (var element in root.EnumerateArray())
            {
                index++;
                items.Add(MapItem(element, index));
            }

            return items;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in new[] { "indexes", "items", "results", "value" })
            {
                if (TryGetProperty(root, candidate, out var property) && property.ValueKind == JsonValueKind.Array)
                {
                    return ParseIndexList(property);
                }
            }
        }

        return Array.Empty<EventStoreIndexListItemViewModel>();
    }

    private static EventStoreIndexListItemViewModel MapItem(JsonElement element, int fallbackIndex)
    {
        var name = GetDisplayName(element, fallbackIndex);
        var status = GetStatus(element);
        var summary = BuildSummary(element);
        return new EventStoreIndexListItemViewModel(name, status, summary, element.GetRawText());
    }

    private static bool TryDescribe(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue.ToString(CultureInfo.InvariantCulture) : element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string ToDisplayName(string propertyName) =>
        propertyName.Replace('_', ' ');
}
