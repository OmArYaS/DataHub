namespace DataHub.Tests.TestSupport;

/// <summary>
/// Controllable clock for the time-dependent services (sync due-picking,
/// cache stamps, ledger timestamps). Frozen at construction; a test moves it
/// forward with <see cref="Advance"/> instead of sleeping.
/// </summary>
public sealed class TestClock(DateTimeOffset startUtc) : TimeProvider
{
    public static TestClock At(DateTimeOffset startUtc) => new(startUtc);

    public DateTimeOffset NowUtc { get; private set; } = startUtc;

    public TestClock Advance(TimeSpan delta)
    {
        NowUtc = NowUtc.Add(delta);
        return this;
    }

    public override DateTimeOffset GetUtcNow() => NowUtc;
}
