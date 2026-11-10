using Nexify.Presentation;
using Xunit;

namespace Nexify.Presentation.Tests;

public class ShutdownSequenceTests
{
    [Fact]
    public void ThrowingStep_DoesNotSkipLaterSteps_AndIsReported()
    {
        var ran = new List<string>();
        var errors = new List<(string, Exception)>();

        var failed = ShutdownSequence.Run(
            (name, ex) => errors.Add((name, ex)),
            ("settings", () => { ran.Add("settings"); throw new InvalidOperationException("boom"); }),
            ("theme", () => ran.Add("theme")),
            ("engine", () => { ran.Add("engine"); throw new NotSupportedException(); }),
            ("log", () => ran.Add("log")));

        Assert.Equal(["settings", "theme", "engine", "log"], ran);
        Assert.Equal(["settings", "engine"], errors.Select(e => e.Item1));
        Assert.IsType<InvalidOperationException>(errors[0].Item2);
        Assert.Equal(2, failed);
    }

    [Fact]
    public void ThrowingErrorHandler_DoesNotStopTheSequence()
    {
        var ran = new List<string>();

        ShutdownSequence.Run(
            (_, _) => throw new InvalidOperationException("logger broken"),
            ("a", () => throw new InvalidOperationException()),
            ("final", () => ran.Add("final")));

        Assert.Equal(["final"], ran);
    }

    [Fact]
    public void AllStepsSucceed_ReturnsZero()
    {
        var count = 0;
        Assert.Equal(0, ShutdownSequence.Run((_, _) => { }, ("a", () => count++), ("b", () => count++)));
        Assert.Equal(2, count);
    }
}
