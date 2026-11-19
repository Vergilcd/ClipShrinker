using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShrinker.Core.Agent;

/// <summary>
/// Dev (AI editing module): the link between the MCP helper and the open Clip Shrinker window. One JSON object per line:
/// the helper sends { "tool": name, "args": {...} } and gets the tool result back. The helper keeps its connection open
/// while its Claude session runs, so the app can show "AI agent connected". Only the same Windows user can connect.
/// </summary>
public sealed class AgentPipeServer : IDisposable
{
    readonly Func<IAgentHost> host;
    readonly CancellationTokenSource cts = new();
    int connections;

    /// <summary>The number of connected helpers changed (raised on a background thread).</summary>
    public event Action<int>? ConnectionsChanged;
    public int Connections => connections;

    public AgentPipeServer(Func<IAgentHost> host) => this.host = host;

    public void Start() => _ = Task.Run(AcceptLoop);

    async Task AcceptLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            NamedPipeServerStream ps;
            try
            {
                ps = new NamedPipeServerStream(AgentTools.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                               PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await ps.WaitForConnectionAsync(cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(1000); continue; }       // (another app instance owns the name)
            _ = Task.Run(() => Serve(ps));
        }
    }

    void Serve(NamedPipeServerStream ps)
    {
        ConnectionsChanged?.Invoke(Interlocked.Increment(ref connections));
        try
        {
            using (ps)
            using (var reader = new StreamReader(ps, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(ps, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    JsonObject reply;
                    try
                    {
                        var req = JsonNode.Parse(line) as JsonObject ?? [];
                        string tool = req["tool"]?.GetValue<string>() ?? "";
                        reply = tool == "" ? new JsonObject { ["ok"] = true }                        // (a hello / ping)
                                           : AgentTools.Call(host(), tool, req["args"] as JsonObject ?? []);
                    }
                    catch (JsonException) { reply = AgentTools.Content([AgentTools.Text("Bad request")], isError: true); }
                    writer.WriteLine(reply.ToJsonString());
                }
            }
        }
        catch (IOException) { }                                              // the helper went away
        catch (ObjectDisposedException) { }
        finally { ConnectionsChanged?.Invoke(Interlocked.Decrement(ref connections)); }
    }

    public void Dispose() => cts.Cancel();
}

/// <summary>The helper's side: connects to the app when it is open (and again after it restarts), and forwards tool calls.</summary>
public sealed class AgentPipeClient : IDisposable
{
    readonly object gate = new();
    NamedPipeClientStream? pipe;
    StreamReader? reader;
    StreamWriter? writer;
    volatile bool stop;

    public bool Connected { get { lock (gate) return pipe is { IsConnected: true }; } }

    /// <summary>Tries to connect now, then keeps trying every 2 s in the background while not connected.</summary>
    public void Start()
    {
        TryConnect();
        new Thread(() =>
        {
            while (!stop)
            {
                Thread.Sleep(2000);
                if (!Connected) TryConnect();
                else Ping();
            }
        }) { IsBackground = true, Name = "agent link" }.Start();
    }

    void TryConnect()
    {
        lock (gate)
        {
            if (pipe is { IsConnected: true }) return;
            Drop();
            var p = new NamedPipeClientStream(".", AgentTools.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            try { p.Connect(300); }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { p.Dispose(); return; }
            pipe = p;
            reader = new StreamReader(p, new UTF8Encoding(false));
            writer = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }
    }

    /// <summary>Notices a closed app (a broken pipe) between calls.</summary>
    void Ping()
    {
        if (!Monitor.TryEnter(gate)) return;                                 // a call is running: the link is alive
        try
        {
            writer!.WriteLine("{}");
            if (reader!.ReadLine() is null) Drop();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NullReferenceException) { Drop(); }
        finally { Monitor.Exit(gate); }
    }

    void Drop()
    {
        try { pipe?.Dispose(); } catch (IOException) { }
        (pipe, reader, writer) = (null, null, null);
    }

    /// <summary>The tool result from the app, or null when the app is not connected (then the helper works on files).</summary>
    public JsonObject? TryCall(string tool, JsonObject args)
    {
        if (!Connected) TryConnect();
        lock (gate)
        {
            if (pipe is not { IsConnected: true }) return null;
            try
            {
                writer!.WriteLine(new JsonObject { ["tool"] = tool, ["args"] = args.DeepClone() }.ToJsonString());
                if (reader!.ReadLine() is { } line) return JsonNode.Parse(line) as JsonObject;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or JsonException) { }
            Drop();
            return null;
        }
    }

    public void Dispose() { stop = true; lock (gate) Drop(); }
}
