namespace TaskLinkPro.Api.Utils;

public static class ETagHelper
{
    public static string ToETag(byte[] rowVersion) =>
        $"W/\"{Convert.ToBase64String(rowVersion)}\"";

    public static bool TryParseIfMatch(string? ifMatchHeader, out byte[]? rowVersion)
    {
        rowVersion = null;
        if (string.IsNullOrWhiteSpace(ifMatchHeader)) return false;
        // Expect: W/"<base64>"
        var parts = ifMatchHeader.Split('"');
        if (parts.Length < 2) return false;
        try {
            rowVersion = Convert.FromBase64String(parts[1]);
            return true;
        } catch { return false; }
    }
}
