using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BPP.Core.Tests")]

namespace BPP.Core;

/// <summary>
/// Forwards raw TCP (opaque TLS) from one listener per given LAN IPv4 address to a fixed target.
/// Never binds IPAddress.Any or loopback: WSL's localhost forwarding owns 127.0.0.1.
/// </summary>
public sealed class RelaySet : IAsyncDisposable
{
    private readonly int _port;
    private readonly IPEndPoint _target;
    private readonly bool _allowLoopback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<IPAddress, Listener> _listeners = new();
    private bool _disposed;

    public RelaySet(int port, IPEndPoint target) : this(port, target, allowLoopbackForTests: false) { }

    internal RelaySet(int port, IPEndPoint target, bool allowLoopbackForTests)
    {
        _port = port;
        _target = target;
        _allowLoopback = allowLoopbackForTests;
    }

    public event Action<IPAddress, Exception>? BindFailed;

    public IReadOnlyCollection<IPAddress> Active
    {
        get { lock (_lock) return _listeners.Keys.ToArray(); }
    }

    internal int BoundPort(IPAddress address)
    {
        lock (_lock) return _listeners[address].Port;
    }

    public async Task UpdateAsync(IEnumerable<IPAddress> listenOn)
    {
        var wanted = listenOn
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork
                        && !a.Equals(IPAddress.Any)
                        && !a.Equals(IPAddress.Broadcast)
                        && (_allowLoopback || !IPAddress.IsLoopback(a)))
            .Distinct()
            .ToList();

        var failures = new List<(IPAddress, Exception)>();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            List<Listener> removed;
            lock (_lock)
            {
                removed = _listeners.Where(kv => !wanted.Contains(kv.Key)).Select(kv => kv.Value).ToList();
                foreach (var l in removed) _listeners.Remove(l.Address);
            }
            foreach (var l in removed) await SafeDisposeAsync(l).ConfigureAwait(false);

            foreach (var address in wanted)
            {
                lock (_lock) if (_listeners.ContainsKey(address)) continue;

                Listener listener;
                try
                {
                    listener = Listener.Start(address, _port, _target);
                }
                catch (Exception ex)
                {
                    failures.Add((address, ex));
                    continue;
                }
                lock (_lock) _listeners[address] = listener;
            }
        }
        finally { _gate.Release(); }

        // raised outside the gate so subscribers may call back into the set
        foreach (var (address, ex) in failures) RaiseBindFailed(address, ex);
    }

    private static async Task SafeDisposeAsync(Listener l)
    {
        try { await l.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { LogUnexpected("closing a listener", ex); } // keep disposing the remaining listeners
    }

    private void RaiseBindFailed(IPAddress address, Exception ex)
    {
        try { BindFailed?.Invoke(address, ex); }
        catch (Exception subscriberEx) { LogUnexpected("BindFailed subscriber", subscriberEx); } // must not break the relay
    }

    /// <summary>How a relayed connection normally ends: reset, EOF race, cancellation, socket closed under a read.</summary>
    private static bool IsConnectionEnd(Exception ex)
        => ex is SocketException or IOException or OperationCanceledException or InvalidOperationException;

    /// <summary>The relay has no logger of its own; unexpected errors go to the trace listeners with their stack.</summary>
    private static void LogUnexpected(string what, Exception ex) => Trace.TraceError($"BPP relay: {what}: {ex}");

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            List<Listener> all;
            lock (_lock)
            {
                all = _listeners.Values.ToList();
                _listeners.Clear();
            }
            foreach (var l in all) await SafeDisposeAsync(l).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private sealed class Listener : IAsyncDisposable
    {
        private readonly TcpListener _tcp;
        private readonly IPEndPoint _target;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _connLock = new();
        private readonly HashSet<Connection> _connections = new();
        private Task _acceptLoop = Task.CompletedTask;

        public IPAddress Address { get; }
        public int Port { get; }

        private Listener(IPAddress address, TcpListener tcp, IPEndPoint target)
        {
            Address = address;
            _tcp = tcp;
            _target = target;
            Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        }

        public static Listener Start(IPAddress address, int port, IPEndPoint target)
        {
            var tcp = new TcpListener(address, port);
            try { tcp.Start(); }
            catch { tcp.Stop(); throw; }
            var l = new Listener(address, tcp, target);
            l._acceptLoop = Task.Run(l.AcceptLoopAsync);
            return l;
        }

        private async Task AcceptLoopAsync()
        {
            var ct = _cts.Token;
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _tcp.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (Exception) when (ct.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; } // listener stopped
                catch (SocketException)
                {
                    // transient accept error; back off so a persistent one cannot spin
                    try { await Task.Delay(100, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                var conn = new Connection(client, _target);
                lock (_connLock)
                {
                    if (ct.IsCancellationRequested) { conn.Close(); return; }
                    _connections.Add(conn);
                }
                _ = conn.RunAsync(ct).ContinueWith(_ =>
                {
                    lock (_connLock) _connections.Remove(conn);
                }, TaskScheduler.Default);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _tcp.Stop();
            Connection[] conns;
            lock (_connLock) conns = _connections.ToArray();
            foreach (var c in conns) c.Close();
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (Exception ex) { LogUnexpected("accept loop", ex); } // the loop handles its expected errors itself
            foreach (var c in conns) await c.Completion.ConfigureAwait(false);
            _cts.Dispose();
        }
    }

    private sealed class Connection
    {
        private readonly TcpClient _client;
        private readonly TcpClient _upstream = new();
        private readonly IPEndPoint _target;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Connection(TcpClient client, IPEndPoint target)
        {
            _client = client;
            _target = target;
        }

        /// <summary>Never faults.</summary>
        public Task Completion => _done.Task;

        public void Close()
        {
            // Close must never throw: it runs in RunAsync's finally and while a listener is being disposed
            try { _client.Close(); } catch (Exception ex) { LogUnexpected("closing the client socket", ex); }
            try { _upstream.Close(); } catch (Exception ex) { LogUnexpected("closing the upstream socket", ex); }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await _upstream.ConnectAsync(_target, ct).ConfigureAwait(false);
                var a = _client.GetStream();
                var b = _upstream.GetStream();
                var t1 = a.CopyToAsync(b, ct);
                var t2 = b.CopyToAsync(a, ct);
                // either direction ending (EOF, error, cancel) tears down both sides
                await Task.WhenAny(t1, t2).ConfigureAwait(false);
                Close();
                await Task.WhenAll(Observe(t1), Observe(t2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsConnectionEnd(ex))
            {
                // connect failure, reset, cancellation: all just end the connection
            }
            catch (Exception ex)
            {
                LogUnexpected("connection", ex);
            }
            finally
            {
                Close();
                _done.TrySetResult();
            }
        }

        private static async Task Observe(Task t)
        {
            try { await t.ConfigureAwait(false); }
            catch (Exception ex) when (IsConnectionEnd(ex)) { /* the other direction ended first */ }
            catch (Exception ex) { LogUnexpected("copy", ex); }
        }
    }
}
