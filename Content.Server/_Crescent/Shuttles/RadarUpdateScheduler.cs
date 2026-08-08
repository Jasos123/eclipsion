namespace Content.Server.Shuttles.Systems;

/// <summary>
/// Schedules radar refreshes at a stable rate and spreads consoles across ticks.
/// </summary>
internal static class RadarUpdateScheduler
{
    /// <summary>Number of offsets available when staggering the first refresh.</summary>
    public const int StaggerBuckets = 64;

    /// <summary>
    /// Returns the delay between updates. <see cref="TimeSpan.Zero"/> means every tick.
    /// </summary>
    /// <param name="uiTps">Requested updates per second. Zero or negative disables rate limiting.</param>
    /// <param name="serverTickRate">Server tick rate, which caps the effective update rate.</param>
    public static TimeSpan GetPeriod(float uiTps, int serverTickRate)
    {
        var effectiveTps = serverTickRate > 0 ? MathF.Min(uiTps, serverTickRate) : uiTps;

        return effectiveTps <= 0f
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(1d / effectiveTps);
    }

    /// <summary>
    /// Checks whether a console is due for an update and advances its deadline.
    /// </summary>
    /// <param name="nextUpdate">
    /// The console's deadline. <see cref="TimeSpan.Zero"/> schedules its first staggered update.
    /// </param>
    /// <param name="curTime">Current game time.</param>
    /// <param name="period">Result of <see cref="GetPeriod"/>.</param>
    /// <param name="staggerSeed">A per-console value used to stagger the first update.</param>
    public static bool TryConsume(ref TimeSpan nextUpdate, TimeSpan curTime, TimeSpan period, int staggerSeed)
    {
        if (period <= TimeSpan.Zero)
            return true;

        if (nextUpdate == TimeSpan.Zero)
        {
            // Opening the UI already sends a full state, so the first scheduled refresh can be staggered.
            var bucket = Math.Abs(staggerSeed) % StaggerBuckets;
            nextUpdate = curTime + period * (bucket / (double) StaggerBuckets);
            return false;
        }

        if (curTime < nextUpdate)
            return false;

        // Advance from the deadline to avoid drifting toward tick boundaries.
        var next = nextUpdate + period;

        // Skip missed updates after a stall instead of queuing a catch-up burst.
        nextUpdate = next <= curTime ? curTime + period : next;
        return true;
    }
}
