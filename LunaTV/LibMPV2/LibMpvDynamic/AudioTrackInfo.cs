using System.Collections.Generic;

namespace LunaTV.LibMPV2.LibMpvDynamic;

public class AudioTrackInfo
{
    public int Id { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsSelected { get; set; }
    public int? FfIndex { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title);

        if (!string.IsNullOrWhiteSpace(Language)) parts.Add($"[{Language}]");

        parts.Add($"(ID: {Id})");

        if (IsSelected) parts.Add("*");

        return string.Join(" ", parts);
    }
}