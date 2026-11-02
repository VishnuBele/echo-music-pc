namespace EchoMusic.Core;

internal enum TrackEndDecision
{
    AllowEnd,
    RetryStream,
    StopAfterRetries,
}

/// <summary>
/// Distinguishes a real media end from a decoder/CDN stream that ends well before the
/// canonical track duration. The source duration is considered too, so a short container
/// cannot cause a long catalog track to be silently skipped.
/// </summary>
internal sealed class PlaybackCutoffGuard
{
    private const double EndToleranceSeconds = 3.0;
    private const double MinimumRunWithoutDurationSeconds = 8.0;
    private const int MaxRetriesPerTrack = 2;
    private readonly object _sync = new();
    private string? _trackId;
    private int _retryCount;

    public void Reset(string? trackId)
    {
        lock (_sync)
        {
            _trackId = trackId;
            _retryCount = 0;
        }
    }

    public TrackEndDecision Evaluate(
        string? trackId,
        double positionSeconds,
        double canonicalDurationSeconds,
        double sourceDurationSeconds,
        out double expectedDurationSeconds,
        out int retryAttempt)
    {
        lock (_sync)
        {
            if (!string.Equals(trackId, _trackId, StringComparison.Ordinal))
            {
                _trackId = trackId;
                _retryCount = 0;
            }

            double canonical = IsUsableDuration(canonicalDurationSeconds) ? canonicalDurationSeconds : 0;
            double source = IsUsableDuration(sourceDurationSeconds) ? sourceDurationSeconds : 0;
            expectedDurationSeconds = Math.Max(canonical, source);
            double safePosition = double.IsFinite(positionSeconds) ? Math.Max(0, positionSeconds) : 0;
            retryAttempt = _retryCount;

            bool durationUnknown = expectedDurationSeconds <= 0;
            bool endedTooEarly = durationUnknown
                ? safePosition < MinimumRunWithoutDurationSeconds
                : expectedDurationSeconds - safePosition > EndToleranceSeconds;
            if (!endedTooEarly)
                return TrackEndDecision.AllowEnd;

            if (_retryCount < MaxRetriesPerTrack)
            {
                retryAttempt = ++_retryCount;
                return TrackEndDecision.RetryStream;
            }

            return TrackEndDecision.StopAfterRetries;
        }
    }

    private static bool IsUsableDuration(double value) => double.IsFinite(value) && value > 0;
}
