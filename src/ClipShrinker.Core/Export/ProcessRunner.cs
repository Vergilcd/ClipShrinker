using System.Diagnostics;
using System.Text;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Export;

/// <summary>Runs a helper program to the end, optionally streaming its output lines (stdout and stderr together).</summary>
public interface IToolRunner
{
    /// <returns>The exit code.</returns>
    int Run(IReadOnlyList<string> cmd, string? workingDir = null, Action<string>? onLine = null);
    void Kill();
}

/// <summary>
/// Real ffmpeg / RIFE processes (reference Encoder._run and the Popen calls in _rife_prepare).
/// One process at a time; Kill() stops the current one and its children.
/// </summary>
public sealed class ProcessRunner(Action<double>? progress = null, Action<string>? status = null) : IFfmpegRunner, IToolRunner
{
    readonly Action<double> progressCb = progress ?? (_ => { });
    readonly Action<string> statusCb = status ?? (_ => { });
    readonly object gate = new();
    Process? current;

    /// <summary>Every command started, for logs and the CLI's --log-commands.</summary>
    public List<IReadOnlyList<string>> Started { get; } = [];

    Process Start(IReadOnlyList<string> cmd, string? workingDir)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = false,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDir is not null) psi.WorkingDirectory = workingDir;
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        var p = new Process { StartInfo = psi };
        lock (gate)
        {
            Started.Add(cmd.ToList());
            p.Start();
            current = p;
        }
        return p;
    }

    /// <summary>Reads stdout and stderr line by line on two threads and hands every line to onLine (serialised).</summary>
    static void Pump(Process p, Action<string> onLine)
    {
        var sync = new object();
        void Read(StreamReader r)
        {
            string? line;
            while ((line = r.ReadLine()) is not null) lock (sync) onLine(line);
        }
        var t1 = Task.Run(() => Read(p.StandardOutput));
        var t2 = Task.Run(() => Read(p.StandardError));
        Task.WaitAll(t1, t2);
        p.WaitForExit();
    }

    public (bool Ok, string Log) Run(IReadOnlyList<string> cmd, double duration, string label, double pbase, double pspan)
    {
        using var p = Start(cmd, null);
        var tail = new List<string>();
        Pump(p, raw =>
        {
            string line = raw.Trim();
            if (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms="))
            {
                if (long.TryParse(line[(line.IndexOf('=') + 1)..], out long us))
                {
                    double frac = Math.Min(1.0, us / 1_000_000.0 / duration);
                    progressCb(pbase + pspan * frac);
                    statusCb($"{label}: {F(frac * 100, 0)}%");
                }
            }
            else if (!line.Contains('='))
            {
                tail.Add(line);
                if (tail.Count > 25) tail.RemoveAt(0);
            }
        });
        lock (gate) current = null;
        return (p.ExitCode == 0, string.Join("\n", tail));
    }

    public int Run(IReadOnlyList<string> cmd, string? workingDir = null, Action<string>? onLine = null)
    {
        using var p = Start(cmd, workingDir);
        Pump(p, onLine ?? (_ => { }));
        lock (gate) current = null;
        return p.ExitCode;
    }

    public void Kill()
    {
        lock (gate)
        {
            try
            {
                if (current is { HasExited: false }) current.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
