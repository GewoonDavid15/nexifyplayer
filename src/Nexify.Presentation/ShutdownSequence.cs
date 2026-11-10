namespace Nexify.Presentation;

/// <summary>Runs shutdown steps in order, each isolated: a failing step is reported and never skips the later ones.</summary>
public static class ShutdownSequence
{
    /// <summary>Runs every step; exceptions go to <paramref name="onError"/> (whose own failures are swallowed). Returns the number of failed steps.</summary>
    public static int Run(Action<string, Exception> onError, params (string Name, Action Step)[] steps)
    {
        ArgumentNullException.ThrowIfNull(onError);
        var failed = 0;
        foreach (var (name, step) in steps)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                failed++;
                try
                {
                    onError(name, ex);
                }
                catch (Exception)
                {
                    // Reporting must never stop the shutdown sequence (e.g. the logger itself is broken).
                }
            }
        }

        return failed;
    }
}
