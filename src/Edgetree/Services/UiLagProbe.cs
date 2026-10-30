using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SidebarExplorer.App.Services;

// Where the UI thread goes when the app feels heavy (2026-10-29, on report:
// scrolling and the panel opening and closing felt less crisp, with nothing
// on screen to point at).
//
// The panel's own stall line (MainWindow.StartUiStallWatch) says how long
// the thread was gone, but only while the panel is open, only past 200ms,
// with no clock time on the line and nothing about where the time went. Two
// months of it in viewerload.log answered one question - stalls past 200ms
// per hundred selections did not rise from August (30-75) to now (44-62 on
// 10-28 and 10-29), so nothing has piled up THERE - and could not answer the
// one asked, because a "slightly heavy" feel lives below 200ms, and in the
// tree with the panel shut. Its timer also runs at Background priority,
// which a stream of wheel input starves without any single thing freezing,
// so a long gap there is not by itself a frame that never came.
//
// This one runs for the life of the process and times what the thread
// actually does, one item at a time:
//   - every dispatcher operation, by name: layout and render come out as
//     MediaContext, a DispatcherTimer as its Tick handler, a BeginInvoke as
//     the method that queued it;
//   - every input event the input manager processes (wheel, mouse, key) and
//     what it landed on. Handlers run inside window message processing,
//     which is no dispatcher operation, so the two halves together cover
//     the thread.
// A single item of 50ms or more is a frame or three that never came, and is
// written with its clock time, so it can be laid beside click.log. From
// 100ms the caller's context goes on the line too (realized tree rows, the
// panel). A gap of 200ms that no single item explains - work the hooks
// cannot see, or a long streak of small items - gets a line of its own.
// Every ten minutes a summary ranks where the time went.
//
// Lines are queued and written from a thread-pool timer: file I/O on the UI
// thread is a cost of its own, and an instrument must not be what it
// measures. Compiled out of Release builds (Conditional), like ExitLog.
internal static class UiLagProbe
{
    private const double ItemMs = 50;
    private const double ContextMs = 100;
    private const double GapMs = 200;
    // Longer than this is the machine asleep, not the app.
    private const double SleepMs = 60_000;

    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly Stack<(DispatcherOperation Op, long Start, long Ticks)> Operations = new();
    // Keyed by the staging item, not stacked: an input a pre-process handler
    // cancels never reaches PostProcessInput, and a stack would then pair
    // every later input with the wrong start.
    private static readonly Dictionary<object, (InputEventArgs Input, long Start, long Ticks)> Inputs =
        new(ReferenceEqualityComparer.Instance);
    private static readonly object FlushGate = new();
    private static readonly Dictionary<string, (int Count, double Total, double Max)> Summary = new();

    private static DispatcherTimer? _gapTimer;
    private static DispatcherTimer? _summaryTimer;
    private static System.Threading.Timer? _flush;
    private static Func<string>? _context;
    private static long _lastGapTick;
    private static double _longestSinceGapTick;
    // How many times the gap timer has ticked. An item that saw it tick was
    // not a freeze: something inside it pumped messages (ShowDialog, a
    // MessageBox), and a button whose dialog stayed open 15s would
    // otherwise be written down as a 15s stall.
    private static long _gapTicks;

    // DispatcherOperation keeps the delegate it will run in a private field
    // and exposes no name for it; found by type, so a renamed field still
    // answers. Null leaves the name as the priority alone.
    private static readonly FieldInfo? OperationMethod = typeof(DispatcherOperation)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .FirstOrDefault(f => typeof(Delegate).IsAssignableFrom(f.FieldType));

    private static readonly FieldInfo[] OperationArgs = typeof(DispatcherOperation)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => f.FieldType == typeof(object))
        .ToArray();

    // The Tick event's backing field: by name first, else the one delegate
    // field the class has that holds EventHandlers.
    private static readonly FieldInfo? TimerTick =
        typeof(DispatcherTimer).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(DispatcherTimer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.FieldType == typeof(EventHandler));

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree", "uilag.log");

    [Conditional("DEBUG")]
    public static void Start(Dispatcher dispatcher, Func<string> context)
    {
        if (_gapTimer is not null)
        {
            return;
        }
        _context = context;

        dispatcher.Hooks.OperationStarted += (_, e) => Operations.Push((e.Operation, Stopwatch.GetTimestamp(), _gapTicks));
        dispatcher.Hooks.OperationCompleted += (_, e) => EndOperation(e.Operation, timed: true);
        // Aborted is raised for an operation that never started as well (a
        // stopped timer's pending tick), which is not on the stack at all.
        dispatcher.Hooks.OperationAborted += (_, e) => EndOperation(e.Operation, timed: false);

        InputManager.Current.PreProcessInput += (_, e) =>
        {
            // Cancelled ones are never collected; a handful is harmless, a
            // session's worth is not.
            if (Inputs.Count > 256)
            {
                Inputs.Clear();
            }
            Inputs[e.StagingItem] = (e.StagingItem.Input, Stopwatch.GetTimestamp(), _gapTicks);
        };
        InputManager.Current.PostProcessInput += (_, e) =>
        {
            if (Inputs.Remove(e.StagingItem, out var input))
            {
                double ms = Since(input.Start);
                // Named only when it is going on a line: a mouse move a
                // pixel is an input too, and a string for each would be
                // garbage of the instrument's own making.
                Measured("input", ms >= ItemMs ? DescribeInput(input.Input) : string.Empty, ms, input.Ticks);
            }
        };

        _gapTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _gapTimer.Tick += (_, _) =>
        {
            _gapTicks++;
            double gap = Since(_lastGapTick);
            _lastGapTick = Stopwatch.GetTimestamp();
            if (gap >= GapMs && gap < SleepMs && _longestSinceGapTick < ItemMs)
            {
                Enqueue($"gap   {gap,6:F0} ms  longest single item {_longestSinceGapTick:F0} ms  {_context?.Invoke()}");
                Add("gap (no single item)", gap);
            }
            _longestSinceGapTick = 0;
        };
        _lastGapTick = Stopwatch.GetTimestamp();
        _gapTimer.Start();

        _summaryTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMinutes(10),
        };
        _summaryTimer.Tick += (_, _) => WriteSummary();
        _summaryTimer.Start();

        Enqueue($"--- started  {DateTime.Now:yyyy-MM-dd}  (items >= {ItemMs} ms, gaps >= {GapMs} ms)");
        _flush = new System.Threading.Timer(_ => Flush(), null, 5000, 5000);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    private static void EndOperation(DispatcherOperation operation, bool timed)
    {
        if (!Operations.Any(o => ReferenceEquals(o.Op, operation)))
        {
            return;
        }
        // Nested frames (a dialog's ShowDialog) run operations inside an
        // operation; anything left above this one ended without saying so.
        while (Operations.TryPop(out var top))
        {
            if (ReferenceEquals(top.Op, operation))
            {
                if (timed)
                {
                    double ms = Since(top.Start);
                    Measured("op", ms >= ItemMs ? DescribeOperation(operation) : string.Empty, ms, top.Ticks);
                }
                return;
            }
        }
    }

    private static void Measured(string kind, string name, double ms, long ticksAtStart)
    {
        if (_gapTicks != ticksAtStart)
        {
            return;
        }
        if (ms > _longestSinceGapTick)
        {
            _longestSinceGapTick = ms;
        }
        if (ms < ItemMs || ms >= SleepMs)
        {
            return;
        }

        string context = ms >= ContextMs ? $"  {_context?.Invoke()}" : string.Empty;
        Enqueue($"{kind,-5} {ms,6:F0} ms  {name}{context}");
        Add($"{kind} {name}", ms);
    }

    private static void Add(string key, double ms)
    {
        Summary.TryGetValue(key, out var entry);
        Summary[key] = (entry.Count + 1, entry.Total + ms, Math.Max(entry.Max, ms));
    }

    private static void WriteSummary()
    {
        if (Summary.Count == 0)
        {
            Enqueue("summary  nothing over the line in ten minutes");
            return;
        }
        Enqueue($"summary  {Summary.Values.Sum(v => v.Count)} items, {Summary.Values.Sum(v => v.Total) / 1000:F1} s in ten minutes:");
        foreach (var (key, v) in Summary.OrderByDescending(p => p.Value.Total).Take(10))
        {
            Enqueue($"         {v.Count,4} x  total {v.Total,7:F0} ms  max {v.Max,5:F0} ms  {key}");
        }
        Summary.Clear();
    }

    private static string DescribeOperation(DispatcherOperation operation)
    {
        if (OperationMethod?.GetValue(operation) is not Delegate method)
        {
            return $"{operation.Priority} ?";
        }
        // A timer's tick reaches the queue as a STATIC lambda inside
        // DispatcherTimer.Restart on .NET 8, with the timer passed as the
        // operation's argument, so the first day's lines read
        // "DispatcherTimer.Restart" for every timer alike, 2.3s ones
        // included (2026-10-29). Measured in a harness: the argument field
        // holds the timer, and its Tick handlers and interval name it. The
        // delegate's target, or what it captured, is asked as well, for
        // whatever queues a timer some other way.
        if ((OperationArgs.Select(f => f.GetValue(operation)).OfType<DispatcherTimer>().FirstOrDefault()
             ?? method.Target as DispatcherTimer
             ?? TimerHeldBy(method.Target)) is { } timer)
        {
            var handlers = (TimerTick?.GetValue(timer) as Delegate)?.GetInvocationList()
                .Select(h => ShortName(h.Method)) ?? Enumerable.Empty<string>();
            string names = string.Join("+", handlers);
            return $"{operation.Priority} timer {(names.Length > 0 ? names : "?")} every {timer.Interval.TotalMilliseconds:F0}ms";
        }
        return $"{operation.Priority} {ShortName(method.Method)}";
    }

    private static DispatcherTimer? TimerHeldBy(object? target)
        => target?.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => f.GetValue(target))
            .OfType<DispatcherTimer>()
            .FirstOrDefault();

    // A lambda's compiled name carries the method it was written in -
    // "<StartMemoryWatch>b__0" on "<>c__DisplayClass12_0" - which is the
    // part worth reading; the class is the one that holds the display class.
    private static string ShortName(MethodInfo method)
    {
        var type = method.DeclaringType;
        while (type is not null && type.Name.StartsWith('<') && type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }
        string name = method.Name;
        int open = name.IndexOf('<');
        int close = name.IndexOf('>');
        if (open >= 0 && close > open)
        {
            name = name.Substring(open + 1, close - open - 1);
        }
        return $"{type?.Name}.{name}";
    }

    private static string DescribeInput(InputEventArgs input)
    {
        string what = input.RoutedEvent?.Name ?? input.GetType().Name;
        return input.Source is FrameworkElement { Name.Length: > 0 } named
            ? $"{what} on {named.GetType().Name} {named.Name}"
            : $"{what} on {input.Source?.GetType().Name ?? "-"}";
    }

    private static double Since(long start)
        => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

    private static void Enqueue(string line)
        => Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");

    private static void Flush()
    {
        if (Pending.IsEmpty)
        {
            return;
        }
        // The pool timer and the exit hook can arrive together.
        lock (FlushGate)
        {
            var lines = new List<string>();
            while (Pending.TryDequeue(out var line))
            {
                lines.Add(line);
            }
            try
            {
                File.AppendAllLines(LogPath, lines);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
