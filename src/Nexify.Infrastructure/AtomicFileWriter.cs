using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Nexify.Infrastructure.Tests")]

namespace Nexify.Infrastructure;

/// <summary>Writes a file via a temp file so a crash never leaves a half-written original.</summary>
public static class AtomicFileWriter
{
    /// <summary>Test hook: invoked with the temp path after the temp file is flushed, before the swap.</summary>
    internal static Action<string>? FailAfterTempWriteForTests;

    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(contents);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            FailAfterTempWriteForTests?.Invoke(tmp);

            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
