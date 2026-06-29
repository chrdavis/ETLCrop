# ETWCrop — Design

This document explains how ETWCrop crops an ETW `.etl` trace while keeping it valid for
Windows Performance Analyzer (WPA) and PerfView, and records the non-obvious details that
make that possible. For usage, see [README.md](README.md).

---

## Goals and constraints

1. **Produce a smaller, valid `.etl`.** The output must open in WPA/PerfView with no errors.
2. **Keep CPU and memory stacks resolvable** inside the cropped window, even though the
   events that make stacks resolvable are emitted at the very start (and end) of the trace,
   often far outside the window.
3. **Don't require elevation.** Reading and re-logging an existing file is unprivileged;
   only controlling a *live* session needs admin rights, which we never do.
4. **Be testable.** The keep/drop decision is pure logic, separated from the ETW plumbing.

The hard part is goal #2: a naïve "drop everything outside `[start, stop]`" crop yields a
file that loads but shows broken or missing stacks, because it throws away the rundown and
trace-setup records that stacks depend on.

---

## High-level approach

ETWCrop is built on TraceEvent's **`ETWReloggerTraceEventSource`**, which reads an input
`.etl` and writes a new `.etl`, raising a callback per event. For each event we decide
whether to **copy it through verbatim** (`relogger.WriteEvent(data)`) or drop it. Because
kept events are copied raw, nothing about them is reinterpreted — a kept stack-walk event is
byte-for-byte the same in the output.

```
input.etl ──► ETWReloggerTraceEventSource ──► AllEvents callback ──► WriteEvent ──► output.etl
													 │
													 ├─ in window?            → keep
													 ├─ metadata/rundown?     → keep (always)
													 └─ otherwise             → drop
```

The pipeline has three cooperating pieces:

| Piece | File | Responsibility |
| --- | --- | --- |
| Decision logic | [`EtlCropFilter`](ETWCrop/EtlCropFilter.cs) | Pure "should this event be kept?" — unit tested. |
| Driver | [`EtlCropper`](ETWCrop/EtlCropper.cs) | Runs the relogger, classifies events, applies re-timing/rebasing. |
| Timestamp rewriter | [`ReloggerRetimer`](ETWCrop/ReloggerRetimer.cs) | Reflection/COM helper to change an event's timestamp and embedded payload timestamps. |

---

## What "keep" means: metadata and the effective time

### Metadata is always kept

`EtlCropFilter.ShouldInclude` keeps an event when **either**:

- it is a **metadata event** and `KeepMetadataEvents` is on, **or**
- its *effective time* is within `[start, stop]`.

"Metadata" here is the set of records WPA needs to attribute, symbolicate, and associate
stacks, independent of the time window:

- **Process / Thread / Image (module)** events, including their **DCStart/DCStop** rundown
  variants — needed to map addresses to processes/modules.
- **Stack-key definitions / rundown** — needed to resolve *compressed* (cached) stacks,
  where a sample carries a key and the definition maps that key to frames.
- **Sampled-profile interval** and the one-time **trace-setup** groups at the start of the
  trace: the kernel **EventTrace header** group
  (`68fdd900-4a3e-11d1-84f4-0000f80464e3`) and **SystemConfig**
  (`01853a65-418f-4f36-aefc-dc0f1d2fd235`). WPA's sampled-profile stack association
  depends on these even when the crop window begins long after they were emitted.

These are recognized in `EtlCropper` by strong type
(`ProcessTraceData`, `ThreadTraceData`, `ImageLoadTraceData`, `StackWalkDefTraceData`,
`SampledProfileIntervalTraceData`) or by task GUID for the two setup groups.

To make the kernel events surface as their strongly-typed forms in the `AllEvents`
callback, `EtlCropper` registers a `KernelTraceEventParser` and subscribes empty handlers to
the relevant templates (`RegisterKernelTemplates`). The events are still inspected and
written through `AllEvents`; the empty handlers only ensure the typed objects are produced.

### Effective time and stacks

A stack-walk event is logged *just after* the event it describes, but it must be kept
**exactly when its owning event is kept** — otherwise a sample inside the window could lose
its stack, or a stack could refer to a dropped sample. So `EtlCropper.GetEffectiveTimeRelativeMSec`
uses, for stack events, the timestamp of the event the stack belongs to:

```csharp
StackWalkStackTraceData stack    => stack.EventTimeStampRelativeMSec,
StackWalkRefTraceData   stackRef => stackRef.EventTimeStampRelativeMSec,
_                                => data.TimeStampRelativeMSec,
```

(Stack-key *definitions* are kept unconditionally via the metadata path, not here.)

---

## Ending the trace at the stop time (clamp)

The output `.etl` header's end time follows the **latest event written**. Because metadata
rundown is kept regardless of the window, a crop of `[1000, 2000]` would still contain
end-of-trace rundown emitted at, say, 103,000 ms — so the cropped trace would *report* the
original length even though the interesting data ends at 2000 ms.

`ClampKeptEventsToWindow` (default on, when a finite stop is set) fixes this: kept rundown
events that occur **after** the stop time are **re-timed to the stop time** before being
written. The result ends at the requested window while still carrying the rundown needed for
valid stacks and symbols. The trace's **start** remains the original session start.

This re-timing is done by `ReloggerRetimer.TrySetCurrentEventTime` (see below).

---

## Rebasing (trim leading time) — the subtle one

`RebaseToWindowStart` shifts the whole kept window earlier so that `start` becomes time
zero, removing the empty leading gap. It supersedes clamp (it also trims the end). It is
**off by default**, because rebasing rewrites absolute timestamps and so breaks correlation
with wall-clock time and other logs; you opt in when you specifically want a zero-based
window.

Rebasing each event's *header* timestamp is easy. The trap is that several **kernel events
embed an absolute QPC timestamp in their payload**, in addition to the event's own
timestamp. If only the header is shifted, the two disagree and WPA aborts opening the trace
with a **catastrophic failure (`0x8000FFFF` / `E_UNEXPECTED`)**.

### Events with embedded absolute timestamps

`EtlCropper.HasEmbeddedAbsoluteTimestamp` enumerates them (the embedded value is in the
first eight payload bytes for all of these):

| Event(s) | Embedded value |
| --- | --- |
| `StackWalkStack`, `StackWalkRef` | timestamp of the event the stack describes |
| `DPC` (incl. threaded/timer), `ISR` | the routine's "initial time" |
| `MemoryHardFault` | time the fault began |
| `Registry` (event **version ≥ 2** only) | time the operation began |
| `LastBranchRecord` | capture time |

> DiskIO-style "elapsed" fields are **durations measured by the driver**, not derived from
> the event timestamp, so they must **not** be shifted.

When rebasing, `EtlCropper` passes a flag into `ReloggerRetimer.TryRebaseCurrentEvent` so
the embedded value is shifted by the same delta as the header, keeping the event internally
consistent.

### Post-crop safety net

A rebased output is re-read once (`CountInconsistentEmbeddedTimestamps`) and DPC/ISR events
are checked: their *elapsed time* (event time − embedded initial time) is a real hardware
interval, so it should be roughly `[0, <1 s]`. A value outside `[-1 ms, 1000 ms]` means an
embedded timestamp was left unshifted — exactly the inconsistency that makes WPA abort. The
count is surfaced as `EtlCropResult.EmbeddedTimestampAnomalies`; the CLI returns exit code
`3` and the UI warns. For a correct crop this is always zero (and it's always zero when
rebasing is off). The check is best-effort and never fails the crop itself.

---

## `ReloggerRetimer`: rewriting timestamps via reflection

TraceEvent does not expose a public API to change the timestamp of the event currently being
relogged, so `ReloggerRetimer` reaches into internals. It is deliberately **defensive**:
every reflection lookup is guarded, and if anything is missing (e.g. after a library
upgrade) `IsSupported`/`CanRebase` become `false` and re-timing is silently skipped, leaving
the original behavior intact.

How it works:

- The relogger writes the current event by **injecting its native COM `ITraceEvent`** (the
  private `m_curITraceEvent` field). That COM object exposes `SetTimeStamp`, so setting it
  **before** `WriteEvent` changes the written event's timestamp.
- Timestamps are QPC values; the helper reflects `_QPCFreq` and `sessionStartTimeQPC` to
  convert between relative milliseconds and absolute QPC, and uses the embedded
  `_LARGE_INTEGER.QuadPart` to call `SetTimeStamp`.
- Embedded payload timestamps are patched in place through `TraceEvent.userData` (an
  `IntPtr` to the event's user-data buffer) with `Marshal.ReadInt64`/`WriteInt64` at offset
  0 (the common location for all embedded-timestamp events above).

Two operations are provided:

| Method | Used by | Effect |
| --- | --- | --- |
| `TrySetCurrentEventTime(data, targetMSec)` | clamp | Move the current event to an absolute relative time (used to pull late rundown back to the stop time). |
| `TryRebaseCurrentEvent(data, shiftMSec, maxMSec, shiftEmbedded)` | rebase | Shift the header by `shiftMSec`, clamped to `[sessionStart, sessionStart + maxMSec]`; optionally shift the embedded payload timestamp by the same delta. |

Because the output header's **start** is pinned to the original session start by the
relogger, re-timing events cannot move the trace's start earlier — rebasing achieves a
zero-based window by shifting the *events* instead.

---

## Cancellation

`Crop` accepts a `CancellationToken`. Inside the per-event callback, if cancellation is
requested it calls `relogger.StopProcessing()`. The native relogger aborts its
`ProcessTrace()` by raising a `COMException` with HRESULT
`0x800704C7` (`HRESULT_FROM_WIN32(ERROR_CANCELLED)`).

`EtlCropper` wraps the `Process()` call and translates that specific COM exception into a
standard `OperationCanceledException`:

```csharp
try { relogger.Process(); }
catch (COMException ex)
{
	if (ex.HResult == ErrorCancelledHResult && cancellationToken.IsCancellationRequested)
		throw new OperationCanceledException(cancellationToken);
	throw;
}
```

A **plain (unfiltered) `catch`** is used on purpose: with an exception *filter*
(`catch ... when`), Visual Studio's "Just My Code" does not treat the catch as a definite
handler and still breaks on the COM exception as "unhandled in user code." The unfiltered
catch makes it a definite user-code handler, so a clean cancel does not break the debugger,
while genuine COM failures are rethrown unchanged.

The WPF app runs the crop on a background thread (`Task.Run`) with a
`CancellationTokenSource`, swaps the Crop button for a Cancel button while busy, and reports
cancellation as a normal (non-error) status.

---

## Progress reporting

The total event count is not known up front, but the trace **duration** is available cheaply
from the header (`SessionEndTimeRelativeMSec`), even for multi-gigabyte files. `EtlCropper`
reads it via `TryReadDurationMSec` and reports `EtlCropProgress` snapshots every
`ProgressInterval` (50,000) events. Because events are processed in **time order**, the most
recent event's relative time advances monotonically, so `EtlCropProgress.PercentComplete`
(current time ÷ total duration, clamped to 0–100) gives a stable determinate percentage for
both the CLI and the UI.

`EtlCropper.ReadTraceInfo` exposes the same header-only timing (`EtlTraceInfo`) so the UI can
size its slider before any crop runs.

---

## Testing strategy

The keep/drop decision and the CLI argument parsing are the parts worth unit-testing without
a real ETL file or live session:

- **`EtlCropFilter`** is a pure static function over `(effectiveTime, isMetadata, options)`.
  Tests cover window boundaries (inclusive), metadata-always-kept, metadata-dropped-when-
  disabled, and open-ended stop.
- **`EtlCropOptions.Validate`** is tested for NaN, negative start, and start > stop.
- **`CliArguments`** parsing/`ToCropOptions` is tested for all flags, positional fallback,
  defaults, and error cases.

The relogger-driven parts (`EtlCropper`, `ReloggerRetimer`) are validated end-to-end against
real traces, including opening cropped/rebased outputs with the actual WPA processing engine
to confirm stacks survive and the `0x8000FFFF` failure does not occur.

---

## File map

| File | Summary |
| --- | --- |
| [`ETWCrop/EtlCropper.cs`](ETWCrop/EtlCropper.cs) | Crop driver, event classification, metadata/rebase rules, post-crop check, `ReadTraceInfo`. |
| [`ETWCrop/EtlCropFilter.cs`](ETWCrop/EtlCropFilter.cs) | Pure keep/drop decision. |
| [`ETWCrop/EtlCropOptions.cs`](ETWCrop/EtlCropOptions.cs) | Window + metadata/clamp/rebase options and validation. |
| [`ETWCrop/EtlCropResult.cs`](ETWCrop/EtlCropResult.cs) | Crop summary, including the anomaly count. |
| [`ETWCrop/EtlCropProgress.cs`](ETWCrop/EtlCropProgress.cs) | Progress snapshot with `PercentComplete`. |
| [`ETWCrop/EtlTraceInfo.cs`](ETWCrop/EtlTraceInfo.cs) | Header-only trace timing. |
| [`ETWCrop/ReloggerRetimer.cs`](ETWCrop/ReloggerRetimer.cs) | Reflection/COM timestamp rewriter (clamp + rebase, incl. embedded). |
| [`ETWCrop.Cli/Program.cs`](ETWCrop.Cli/Program.cs) | CLI entry point, output, exit codes. |
| [`ETWCrop.Cli/CliArguments.cs`](ETWCrop.Cli/CliArguments.cs) | Argument parsing and mapping to `EtlCropOptions`. |
| [`ETWCrop.App`](ETWCrop.App) | WPF UI (drag-drop, range slider, progress, cancellation). |
