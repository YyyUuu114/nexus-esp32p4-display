namespace NexusDisplay;

internal sealed class EnergyAccumulator
{
    private const double MaximumCombinedPowerWatts = 20_000.0;
    private long? _lastTimestamp;
    private double? _lastPowerWatts;

    public double KilowattHours { get; private set; }

    public void AddSample(long timestamp, long timestampFrequency, double? combinedPowerWatts)
    {
        if (timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        double? validPower = combinedPowerWatts is >= 0.0 and <= MaximumCombinedPowerWatts &&
                             double.IsFinite(combinedPowerWatts.Value)
            ? combinedPowerWatts
            : null;

        if (_lastTimestamp.HasValue && _lastPowerWatts.HasValue && validPower.HasValue)
        {
            double elapsedSeconds = (timestamp - _lastTimestamp.Value) / (double)timestampFrequency;
            if (elapsedSeconds > 0.0 && elapsedSeconds <= 10.0)
            {
                double averageWatts = (_lastPowerWatts.Value + validPower.Value) / 2.0;
                double increment = averageWatts * elapsedSeconds / 3_600_000.0;
                if (double.IsFinite(increment) && increment >= 0.0)
                    KilowattHours = Math.Max(KilowattHours, KilowattHours + increment);
            }
        }

        _lastTimestamp = timestamp;
        _lastPowerWatts = validPower;
    }
}
