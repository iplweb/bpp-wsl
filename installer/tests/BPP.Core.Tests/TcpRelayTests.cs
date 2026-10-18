using System.Net;
using System.Net.Sockets;
using BPP.Core;

namespace BPP.Core.Tests;

public class TcpRelayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static (TcpListener Listener, Task Loop) StartEchoServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var loop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var c = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        using var client = c;
                        try { await client.GetStream().CopyToAsync(client.GetStream()); }
                        catch (IOException) { }
                        catch (ObjectDisposedException) { }
                    });
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        });
        return (listener, loop);
    }

    private static int EndpointPort(TcpListener l) => ((IPEndPoint)l.LocalEndpoint).Port;

    [Fact]
    public async Task Relays_1MB_both_ways_through_echo_server()
    {
        var (echo, _) = StartEchoServer();
        try
        {
            await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, EndpointPort(echo)), allowLoopbackForTests: true);
            await relay.UpdateAsync(new[] { IPAddress.Loopback });
            Assert.Equal(new[] { IPAddress.Loopback }, relay.Active);

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(Timeout);
            await client.ConnectAsync(IPAddress.Loopback, relay.BoundPort(IPAddress.Loopback), cts.Token);
            var stream = client.GetStream();

            var payload = new byte[1024 * 1024];
            new Random(42).NextBytes(payload);

            var write = Task.Run(async () =>
            {
                await stream.WriteAsync(payload, cts.Token);
                await stream.FlushAsync(cts.Token);
            });
            var received = new byte[payload.Length];
            await stream.ReadExactlyAsync(received, cts.Token);
            await write;

            Assert.True(payload.AsSpan().SequenceEqual(received));
        }
        finally { echo.Stop(); }
    }

    [Fact]
    public async Task Closing_target_side_closes_client_side()
    {
        var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        try
        {
            await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, EndpointPort(target)), true);
            await relay.UpdateAsync(new[] { IPAddress.Loopback });

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(Timeout);
            await client.ConnectAsync(IPAddress.Loopback, relay.BoundPort(IPAddress.Loopback), cts.Token);
            using var accepted = await target.AcceptTcpClientAsync(cts.Token);
            accepted.Close();

            var n = await client.GetStream().ReadAsync(new byte[1], cts.Token);
            Assert.Equal(0, n);
        }
        finally { target.Stop(); }
    }

    [Fact]
    public async Task Empty_update_closes_listener()
    {
        var (echo, _) = StartEchoServer();
        try
        {
            await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, EndpointPort(echo)), true);
            await relay.UpdateAsync(new[] { IPAddress.Loopback });
            var port = relay.BoundPort(IPAddress.Loopback);

            await relay.UpdateAsync(Array.Empty<IPAddress>());

            Assert.Empty(relay.Active);
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(Timeout);
            await Assert.ThrowsAsync<SocketException>(async () =>
                await client.ConnectAsync(IPAddress.Loopback, port, cts.Token));
        }
        finally { echo.Stop(); }
    }

    [Fact]
    public async Task Removing_address_tears_down_active_connection()
    {
        var (echo, _) = StartEchoServer();
        try
        {
            await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, EndpointPort(echo)), true);
            await relay.UpdateAsync(new[] { IPAddress.Loopback });

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(Timeout);
            await client.ConnectAsync(IPAddress.Loopback, relay.BoundPort(IPAddress.Loopback), cts.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 1 }, cts.Token);
            await stream.ReadExactlyAsync(new byte[1], cts.Token);

            await relay.UpdateAsync(Array.Empty<IPAddress>());

            int n;
            try { n = await stream.ReadAsync(new byte[1], cts.Token); }
            catch (IOException) { n = 0; }
            Assert.Equal(0, n);
        }
        finally { echo.Stop(); }
    }

    [Fact]
    public async Task Any_and_loopback_are_ignored_in_public_ctor()
    {
        await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, 1));
        var failures = new List<IPAddress>();
        relay.BindFailed += (a, _) => failures.Add(a);

        await relay.UpdateAsync(new[] { IPAddress.Any, IPAddress.Loopback, IPAddress.IPv6Any, IPAddress.IPv6Loopback });

        Assert.Empty(relay.Active);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Any_is_ignored_even_when_loopback_allowed()
    {
        await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, 1), true);
        await relay.UpdateAsync(new[] { IPAddress.Any });
        Assert.Empty(relay.Active);
    }

    [Fact]
    public async Task Busy_port_raises_BindFailed_without_throwing()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var busyPort = EndpointPort(blocker);

        await using var relay = new RelaySet(busyPort, new IPEndPoint(IPAddress.Loopback, 1), true);
        var failures = new List<(IPAddress, Exception)>();
        relay.BindFailed += (a, e) => failures.Add((a, e));

        await relay.UpdateAsync(new[] { IPAddress.Loopback });

        Assert.Empty(relay.Active);
        var f = Assert.Single(failures);
        Assert.Equal(IPAddress.Loopback, f.Item1);
        Assert.IsAssignableFrom<SocketException>(f.Item2);
    }

    [Fact]
    public async Task Dispose_closes_listeners()
    {
        var (echo, _) = StartEchoServer();
        try
        {
            var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, EndpointPort(echo)), true);
            await relay.UpdateAsync(new[] { IPAddress.Loopback });
            var port = relay.BoundPort(IPAddress.Loopback);

            await relay.DisposeAsync();

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(Timeout);
            await Assert.ThrowsAsync<SocketException>(async () =>
                await client.ConnectAsync(IPAddress.Loopback, port, cts.Token));
        }
        finally { echo.Stop(); }
    }

    [Fact]
    public async Task Rapid_add_remove_cycles_never_throw()
    {
        await using var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, 1), true);
        for (var i = 0; i < 200; i++)
        {
            await relay.UpdateAsync(new[] { IPAddress.Loopback });
            await relay.UpdateAsync(Array.Empty<IPAddress>());
        }
        Assert.Empty(relay.Active);
    }

    [Fact]
    public async Task Update_after_dispose_is_noop()
    {
        var relay = new RelaySet(0, new IPEndPoint(IPAddress.Loopback, 1), true);
        await relay.DisposeAsync();
        await relay.UpdateAsync(new[] { IPAddress.Loopback });
        Assert.Empty(relay.Active);
    }

    [Fact]
    public async Task BindFailed_subscriber_may_call_back_into_set()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        await using var relay = new RelaySet(EndpointPort(blocker), new IPEndPoint(IPAddress.Loopback, 1), true);
        Task? inner = null;
        relay.BindFailed += (_, _) => inner = relay.UpdateAsync(Array.Empty<IPAddress>());

        await relay.UpdateAsync(new[] { IPAddress.Loopback }).WaitAsync(Timeout);
        Assert.NotNull(inner);
        await inner!.WaitAsync(Timeout);
    }
}
