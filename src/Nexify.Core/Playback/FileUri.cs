namespace Nexify.Core.Playback;

/// <summary>
/// Builds a <c>file://</c> URI from a file-system path with every path segment percent-escaped.
/// <c>new Uri(path)</c> is not enough: it leaves existing <c>%XX</c> sequences alone, so a file named
/// <c>a%41b.mp3</c> would turn into <c>aAb.mp3</c>.
/// </summary>
public static class FileUri
{
    public static Uri FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var p = path.Replace('\\', '/');

        string authority = "";
        if (p.StartsWith("//", StringComparison.Ordinal))
        {
            // UNC: //server/share/...
            var slash = p.IndexOf('/', 2);
            authority = slash < 0 ? p[2..] : p[2..slash];
            p = slash < 0 ? "/" : p[slash..];
        }
        else if (!p.StartsWith('/'))
        {
            p = "/" + p; // C:/...
        }

        var segments = p.Split('/').Select((s, i) =>
            i == 1 && authority.Length == 0 && s.Length == 2 && s[1] == ':' && char.IsAsciiLetter(s[0])
                ? s // drive letter
                : Uri.EscapeDataString(s));
        return new Uri("file://" + authority + string.Join('/', segments));
    }
}
