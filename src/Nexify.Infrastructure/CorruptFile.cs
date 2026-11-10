namespace Nexify.Infrastructure;

internal static class CorruptFile
{
    /// <summary>Copies <paramref name="path"/> to *.corrupt.json (overwriting) and deletes the original.</summary>
    public static void Quarantine(string path)
    {
        var corrupt = Path.ChangeExtension(path, ".corrupt.json");
        File.Copy(path, corrupt, overwrite: true);
        File.Delete(path);
    }
}
