using Xunit;

namespace TelegramBotWebApp.Tests.Helpers;

/// <summary>
/// Provides runtime test-skipping backed by <see cref="Skip"/> from the
/// <c>Xunit.SkippableFact</c> package.
/// Use with <c>[SkippableFact]</c> or <c>[SkippableTheory]</c> attributes
/// (not <c>[Fact]</c>) so that the xUnit runner recognises the skip signal.
/// </summary>
public static class SkipIf
{
    /// <summary>
    /// Skips the current test with <paramref name="reason"/> when <paramref name="condition"/> is true.
    /// </summary>
    public static void True(bool condition, string reason)
        => Skip.If(condition, reason);

    /// <summary>Skips the current test when <paramref name="condition"/> is false.</summary>
    public static void False(bool condition, string reason)
        => Skip.IfNot(condition, reason);
}
