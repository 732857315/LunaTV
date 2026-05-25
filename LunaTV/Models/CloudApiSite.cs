using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LunaTV.Models;

public class CloudApiSourceResponse
{
    [JsonPropertyName("cache_time")] public int CacheTime { get; set; }

    [JsonPropertyName("api_site")] public Dictionary<string, CloudApiSite>? ApiSite { get; set; }
}

public class CloudApiSite
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("api")] public string? Api { get; set; }

    [JsonPropertyName("detail")] public string? Detail { get; set; }

    [JsonPropertyName("_comment")] public string? Comment { get; set; }
}