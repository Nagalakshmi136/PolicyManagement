namespace PolicyManagement.Application.Common.Options;

/// <summary>
/// Strongly-typed options bound to the <c>Cache</c> configuration section.
/// Register via <c>services.Configure&lt;CacheOptions&gt;(config.GetSection(CacheOptions.SectionName))</c>
/// in <c>Program.cs</c>.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Number of calendar days from today used as the upper boundary for the
    /// "expiring soon" count in <c>GetPolicySummaryQueryHandler</c>.
    /// Default: 30. Configuration key: <c>Cache__ExpiringSoonDays</c>.
    /// </summary>
    public int ExpiringSoonDays { get; init; } = 30;

    /// <summary>
    /// Sliding cache expiration in seconds. Default: 300.
    /// Configuration key: <c>Cache__SlidingExpirationSeconds</c>.
    /// </summary>
    public int SlidingExpirationSeconds { get; init; } = 300;

    /// <summary>
    /// Absolute cache expiration in seconds. Default: 3600.
    /// Configuration key: <c>Cache__AbsoluteExpirationSeconds</c>.
    /// </summary>
    public int AbsoluteExpirationSeconds { get; init; } = 3600;
}
