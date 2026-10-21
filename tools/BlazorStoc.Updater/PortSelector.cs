using System.Net.NetworkInformation;

namespace BlazorStoc.Updater;

/// <summary>What the machine says about a TCP port; the real one asks Windows for the listeners, the checks give their own.</summary>
public interface IPortProbe
{
    bool IsFree(int port);
}

public sealed class SystemPortProbe : IPortProbe
{
    /// <summary>Free = nothing listens on it, on any address of this computer (a listener on 0.0.0.0 or on ::1 takes the port from 127.0.0.1 too).</summary>
    public bool IsFree(int port) => !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
}

/// <summary>The port asked for and the one chosen (they differ when the first was taken).</summary>
public sealed record PortChoice(int Requested, int Chosen)
{
    public bool Moved => Requested != Chosen;
}

/// <summary>
/// The ports of a new installation (the application and the database server): the initial one when it is free on this server at the moment of the installation, else the next free one
/// (never one in <c>avoid</c>, which holds the port already chosen for the other). Only user ports are used (1024-65535).
/// </summary>
public static class PortSelector
{
    public const int Minimum = 1024;
    public const int Maximum = 65535;
    private const int Window = 500;

    public static PortChoice Choose(int preferred, IReadOnlyCollection<int> avoid, IPortProbe probe)
    {
        if (preferred is < Minimum or > Maximum) throw new ArgumentException($"Port {preferred} is not between {Minimum} and {Maximum}.");
        for (var offset = 0; offset < Window; offset++)
        {
            var port = preferred + offset;
            if (port > Maximum) break;
            if (!avoid.Contains(port) && probe.IsFree(port)) return new PortChoice(preferred, port);
        }
        // Nothing free above the first one (the end of the range, or a block of busy ports): look below it, then from the start of the range.
        for (var port = Math.Min(preferred, Maximum) - 1; port >= Math.Max(Minimum, preferred - Window); port--)
            if (!avoid.Contains(port) && probe.IsFree(port)) return new PortChoice(preferred, port);
        for (var port = Minimum; port <= Maximum; port++)
            if (!avoid.Contains(port) && probe.IsFree(port)) return new PortChoice(preferred, port);
        throw new InvalidOperationException("No free TCP port was found on this server.");
    }
}
