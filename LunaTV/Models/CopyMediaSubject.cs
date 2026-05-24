using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LunaTV.Models;

public class CopyMediaDetail
{
    [JsonPropertyName("url")] public string Url { set; get; }
    [JsonPropertyName("episode")] public string Episode { set; get; }
}

public class CopyMediaSubject
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("medias")] public List<CopyMediaDetail>? Medias { get; set; }
}