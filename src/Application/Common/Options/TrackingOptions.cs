namespace KartDeliveryTrackingService.Application.Common.Options;

/// <summary>Binds the "Tracking" configuration section - design-decisions.md/edge-cases.md's engineering defaults.</summary>
public sealed class TrackingOptions
{
    /// <summary>edge-cases.md "Duplicate Carrier Webhook Delivery" - 7-day baseline dedup-entry TTL.</summary>
    public int DedupEntryTtlDays { get; set; } = 7;

    /// <summary>TRK-6 - grace period added past a terminal status before early-eviction.</summary>
    public int DedupEntryTerminalGraceDays { get; set; } = 30;

    /// <summary>edge-cases.md "Carrier Webhook Failure or Silence" - 6-hour staleness threshold.</summary>
    public int PollingStalenessThresholdHours { get; set; } = 6;

    /// <summary>TRK-5 sweep cadence.</summary>
    public int PollingIntervalMinutes { get; set; } = 15;

    /// <summary>TRK-5 - caps how many stale shipments one sweep processes, so one sweep tick can't run unbounded.</summary>
    public int PollingBatchSize { get; set; } = 100;

    /// <summary>TRK-7 sweep cadence.</summary>
    public int UnmappedStatusEscalationSweepIntervalMinutes { get; set; } = 30;
}
