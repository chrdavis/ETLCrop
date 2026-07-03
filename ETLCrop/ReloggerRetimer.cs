using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;

namespace ETWCrop;

/// <summary>
/// Rewrites the timestamp of the event currently being passed through an
/// <see cref="ETWReloggerTraceEventSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// The relogger writes the event currently being dispatched by injecting its native
/// <c>ITraceEvent</c> (the private <c>m_curITraceEvent</c> field). That COM object exposes
/// <c>SetTimeStamp</c>, so setting it before <see cref="ETWReloggerTraceEventSource.WriteEvent"/>
/// changes the timestamp of the written event. The output ETL header's end time follows the latest
/// written event, so re-timing the end-of-trace rundown back to the stop time pulls the cropped
/// trace's end in to match the requested window.
/// </para>
/// <para>
/// This relies on internal members of the TraceEvent library and an embedded COM interop type, so
/// every reflection lookup is guarded. If anything is missing (for example after a library upgrade),
/// <see cref="IsSupported"/> is <see langword="false"/> and re-timing is silently skipped, leaving
/// the original behaviour intact.
/// </para>
/// </remarks>
internal sealed class ReloggerRetimer
{
    private readonly ETWReloggerTraceEventSource _relogger;
    private readonly FieldInfo? _currentEventField;
    private readonly FieldInfo? _qpcFrequencyField;
    private readonly FieldInfo? _sessionStartField;
    private readonly FieldInfo? _userDataField;
    private readonly MethodInfo? _setTimeStamp;
    private readonly FieldInfo? _quadPartField;
    private readonly object? _largeInteger;
    private readonly object?[] _invokeArguments = new object?[1];
    private long _qpcFrequency;
    private long _sessionStartQpc;

    public ReloggerRetimer(ETWReloggerTraceEventSource relogger)
    {
        _relogger = relogger;

        try
        {
            const BindingFlags instanceMembers = BindingFlags.NonPublic | BindingFlags.Instance;
            Type reloggerType = typeof(ETWReloggerTraceEventSource);

            _currentEventField = reloggerType.GetField("m_curITraceEvent", instanceMembers);
            _qpcFrequencyField = reloggerType.GetField("_QPCFreq", instanceMembers);
            _sessionStartField = reloggerType.GetField("sessionStartTimeQPC", instanceMembers);
            _userDataField = typeof(TraceEvent).GetField("userData", instanceMembers);

            Type? traceEventComType = _currentEventField?.FieldType;
            _setTimeStamp = traceEventComType?.GetMethod("SetTimeStamp");

            // The single parameter is 'ref _LARGE_INTEGER'; its element type has a QuadPart field.
            Type? largeIntegerType = _setTimeStamp?.GetParameters() is { Length: 1 } parameters
                ? parameters[0].ParameterType.GetElementType()
                : null;
            _quadPartField = largeIntegerType?.GetField("QuadPart");
            if (largeIntegerType is not null)
            {
                _largeInteger = Activator.CreateInstance(largeIntegerType);
            }

            IsSupported = _currentEventField is not null
                && _qpcFrequencyField is not null
                && _setTimeStamp is not null
                && _quadPartField is not null
                && _largeInteger is not null;
        }
        catch
        {
            IsSupported = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether timestamp rewriting is available in this environment.
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    /// Gets a value indicating whether the trace can be rebased (also requires the session-start and
    /// payload-pointer fields used by <see cref="TryRebaseCurrentEvent"/>).
    /// </summary>
    public bool CanRebase => IsSupported && _sessionStartField is not null && _userDataField is not null;

    /// <summary>
    /// Shifts the current event earlier by <paramref name="shiftMSec"/> milliseconds (clamped to the
    /// new <c>[0, maxRelativeMSec]</c> window) so the cropped window begins at time zero. Some kernel
    /// events also embed an absolute timestamp in their payload (for example a stack-walk's reference
    /// time, or a DPC/ISR's initial time); when <paramref name="shiftEmbeddedTimestamp"/> is set, that
    /// embedded value - stored in the first eight payload bytes - is shifted by the same amount so it
    /// stays consistent with the (likewise shifted) event. Must be called from the relogger callback
    /// before writing the event.
    /// </summary>
    /// <returns><see langword="true"/> if the event was shifted.</returns>
    public bool TryRebaseCurrentEvent(TraceEvent data, double shiftMSec, double maxRelativeMSec, bool shiftEmbeddedTimestamp)
    {
        if (!CanRebase)
        {
            return false;
        }

        try
        {
            if (_qpcFrequency == 0)
            {
                _qpcFrequency = (long)_qpcFrequencyField!.GetValue(_relogger)!;
                if (_qpcFrequency == 0)
                {
                    return false;
                }
            }

            if (_sessionStartQpc == 0)
            {
                _sessionStartQpc = (long)_sessionStartField!.GetValue(_relogger)!;
            }

            long deltaQpc = (long)(shiftMSec * _qpcFrequency / 1000.0);
            long minQpc = _sessionStartQpc;
            long maxQpc = double.IsPositiveInfinity(maxRelativeMSec)
                ? long.MaxValue
                : _sessionStartQpc + (long)(maxRelativeMSec * _qpcFrequency / 1000.0);

            // Some kernel events embed an absolute QPC in the first 8 payload bytes (a stack-walk's
            // reference time, a DPC/ISR's initial time, etc.); shift it by the same amount so it keeps
            // matching its (shifted) event and the trace stays internally consistent for analyzers.
            if (shiftEmbeddedTimestamp)
            {
                var userData = (IntPtr)(_userDataField!.GetValue(data) ?? (object)IntPtr.Zero);
                if (userData != IntPtr.Zero)
                {
                    long shifted = Clamp(Marshal.ReadInt64(userData) - deltaQpc, minQpc, maxQpc);
                    Marshal.WriteInt64(userData, shifted);
                }
            }

            long targetQpc = Clamp(data.TimeStampQPC - deltaQpc, minQpc, maxQpc);
            object? currentEvent = _currentEventField!.GetValue(_relogger);
            if (currentEvent is null)
            {
                return false;
            }

            _quadPartField!.SetValue(_largeInteger, targetQpc);
            _invokeArguments[0] = _largeInteger;
            _setTimeStamp!.Invoke(currentEvent, _invokeArguments);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;

    /// <summary>
    /// Re-times the event currently being processed to <paramref name="targetRelativeMSec"/>
    /// (milliseconds relative to the session start). Must be called from within the relogger's
    /// event callback, before writing the event.
    /// </summary>
    /// <returns><see langword="true"/> if the timestamp was successfully changed.</returns>
    public bool TrySetCurrentEventTime(TraceEvent data, double targetRelativeMSec)
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            if (_qpcFrequency == 0)
            {
                _qpcFrequency = (long)_qpcFrequencyField!.GetValue(_relogger)!;
                if (_qpcFrequency == 0)
                {
                    return false;
                }
            }

            // Convert the desired relative time to an absolute QPC value, derived from this event's
            // own QPC so we do not need the session-start QPC separately.
            double deltaMSec = targetRelativeMSec - data.TimeStampRelativeMSec;
            long targetQpc = data.TimeStampQPC + (long)(deltaMSec * _qpcFrequency / 1000.0);

            object? currentEvent = _currentEventField!.GetValue(_relogger);
            if (currentEvent is null)
            {
                return false;
            }

            _quadPartField!.SetValue(_largeInteger, targetQpc);
            _invokeArguments[0] = _largeInteger;
            _setTimeStamp!.Invoke(currentEvent, _invokeArguments);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
