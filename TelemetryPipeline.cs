using System.Diagnostics;

namespace NexusDisplay;

internal readonly record struct PipelineStopResult(bool TransportStopped, bool SamplingStopped);

/// <summary>
/// Runs telemetry acquisition and transport on independent dedicated threads.
/// A blocked hardware provider therefore cannot stop the serial heartbeat.
/// </summary>
internal sealed class TelemetryPipeline<T> : IDisposable where T : class
{
    private readonly Func<T> _sample;
    private readonly Action<T, TimeSpan> _transport;
    private readonly Action<Exception> _sampleError;
    private readonly Action<Exception> _transportError;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _transportInterval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _samplingThread;
    private readonly Thread _transportThread;
    private TimedValue _latest;
    private int _started;
    private int _stopRequested;
    private bool _disposed;

    public TelemetryPipeline(
        T initialValue,
        Func<T> sample,
        Action<T, TimeSpan> transport,
        Action<Exception> sampleError,
        Action<Exception> transportError,
        TimeSpan sampleInterval,
        TimeSpan transportInterval)
    {
        ArgumentNullException.ThrowIfNull(initialValue);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sampleError);
        ArgumentNullException.ThrowIfNull(transportError);
        if (sampleInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        if (transportInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(transportInterval));

        _latest = new TimedValue(initialValue, Stopwatch.GetTimestamp());
        _sample = sample;
        _transport = transport;
        _sampleError = sampleError;
        _transportError = transportError;
        _sampleInterval = sampleInterval;
        _transportInterval = transportInterval;
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = "NEXUS hardware sampling",
            Priority = ThreadPriority.Normal
        };
        _transportThread = new Thread(TransportLoop)
        {
            IsBackground = true,
            Name = "NEXUS serial transport",
            Priority = ThreadPriority.AboveNormal
        };
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _samplingThread.Start();
        _transportThread.Start();
    }

    private void SamplingLoop()
    {
        CancellationToken cancellationToken = _stop.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            long cycleStart = Stopwatch.GetTimestamp();
            try
            {
                T value = _sample();
                Volatile.Write(ref _latest, new TimedValue(value, Stopwatch.GetTimestamp()));
            }
            catch (Exception ex)
            {
                _sampleError(ex);
            }

            if (!WaitForRemainder(cycleStart, _sampleInterval, cancellationToken)) break;
        }
    }

    private void TransportLoop()
    {
        CancellationToken cancellationToken = _stop.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            long cycleStart = Stopwatch.GetTimestamp();
            try
            {
                TimedValue current = Volatile.Read(ref _latest);
                TimeSpan age = Stopwatch.GetElapsedTime(current.Timestamp, Stopwatch.GetTimestamp());
                _transport(current.Value, age);
            }
            catch (Exception ex)
            {
                _transportError(ex);
            }

            if (!WaitForRemainder(cycleStart, _transportInterval, cancellationToken)) break;
        }
    }

    private static bool WaitForRemainder(long cycleStart, TimeSpan interval,
                                         CancellationToken cancellationToken)
    {
        TimeSpan remaining = interval - Stopwatch.GetElapsedTime(cycleStart, Stopwatch.GetTimestamp());
        return remaining <= TimeSpan.Zero
            ? !cancellationToken.IsCancellationRequested
            : !cancellationToken.WaitHandle.WaitOne(remaining);
    }

    public PipelineStopResult Stop(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0) _stop.Cancel();
        if (Volatile.Read(ref _started) == 0) return new PipelineStopResult(true, true);

        long deadline = Stopwatch.GetTimestamp() +
                        (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        bool transportStopped = JoinUntil(_transportThread, deadline);
        bool samplingStopped = JoinUntil(_samplingThread, deadline);
        return new PipelineStopResult(transportStopped, samplingStopped);
    }

    private static bool JoinUntil(Thread thread, long deadline)
    {
        if (!thread.IsAlive) return true;
        TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
        if (remaining <= TimeSpan.Zero) return false;
        return thread.Join(remaining);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PipelineStopResult stopped = Stop(TimeSpan.FromSeconds(3));
        if (stopped.TransportStopped && stopped.SamplingStopped) _stop.Dispose();
    }

    private sealed record TimedValue(T Value, long Timestamp);
}
