using System.Net;
using System.Net.Sockets;

namespace Ferrostorm.Net;

/// <summary>
/// TICKET-P1-08b network-conditions harness. A TCP forwarding proxy placed
/// between a lockstep client and the relay that injects one-way delay, random
/// jitter, and occasional long stalls (the TCP-level manifestation of packet
/// loss, which surfaces as retransmit delay on a reliable stream).
///
/// Honest scope note: this validates the lockstep protocol's tolerance of
/// delayed and bursty delivery at the transport layer. It does not exercise
/// kernel/NIC behaviour; a true netem pass on real hardware remains a beta
/// checklist item (gate review doc 13).
///
/// Timing here lives OUTSIDE the simulation: it changes when frames arrive,
/// never what they contain, so determinism is untouched by design.
/// </summary>
public sealed class ChaosProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _targetPort;
    private readonly int _delayMs, _jitterMs, _stallPerMille, _stallMs;
    private readonly Random _timing; // timing randomness only - never sim state
    private volatile bool _running = true;

    public int Port { get; }

    public ChaosProxy(int targetPort, int delayMs, int jitterMs, int stallPerMille, int stallMs, int timingSeed)
    {
        _targetPort = targetPort;
        _delayMs = delayMs; _jitterMs = jitterMs;
        _stallPerMille = stallPerMille; _stallMs = stallMs;
        _timing = new Random(timingSeed);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true }.Start();
    }

    private void AcceptLoop()
    {
        try
        {
            while (_running)
            {
                var inbound = _listener.AcceptTcpClient();
                inbound.NoDelay = true;
                var outbound = new TcpClient();
                outbound.Connect(IPAddress.Loopback, _targetPort);
                outbound.NoDelay = true;
                new Thread(() => Forward(inbound.GetStream(), outbound.GetStream())) { IsBackground = true }.Start();
                new Thread(() => Forward(outbound.GetStream(), inbound.GetStream())) { IsBackground = true }.Start();
            }
        }
        catch { /* listener closed */ }
    }

    private void Forward(NetworkStream from, NetworkStream to)
    {
        var buf = new byte[8192];
        try
        {
            while (_running)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                int wait = _delayMs;
                lock (_timing)
                {
                    if (_jitterMs > 0) wait += _timing.Next(-_jitterMs, _jitterMs + 1);
                    if (_stallPerMille > 0 && _timing.Next(1000) < _stallPerMille) wait += _stallMs;
                }
                if (wait > 0) Thread.Sleep(wait);
                to.Write(buf, 0, n);
                to.Flush();
            }
        }
        catch { /* connection closed */ }
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }
}
