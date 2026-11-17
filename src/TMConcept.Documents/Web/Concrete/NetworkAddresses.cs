using System.Net;
using System.Net.Sockets;

namespace TMConcept.Documents.Web;

/// <summary>
/// Which addresses a document may come from: only the public internet. This computer (localhost, 127.0.0.0/8, ::1), the local network
/// (10/8, 172.16/12, 192.168/16, fc00::/7), link-local addresses (169.254/16 with the cloud metadata address, fe80::/10), shared carrier
/// addresses (100.64/10), "this network" (0/8, ::), multicast and reserved ranges are refused, so a pasted address or a redirect cannot make the
/// app read a router's page or a local service and keep it as a document.
/// </summary>
public static class NetworkAddresses
{
    /// <summary>Whether <paramref name="address"/> is on the public internet.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && b[1] is 18 or 19));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IPv6None.Equals(address) || IPAddress.IPv6Loopback.Equals(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
        }

        return false;
    }

    /// <summary>
    /// Whether the host of an address names this computer or a local network by itself (localhost, *.localhost, *.local, *.internal, a host
    /// without a dot, or an IP address that is not public); a name is otherwise checked when it is resolved (<see cref="DocumentTransport"/>).
    /// </summary>
    public static bool IsLocalHost(string host)
    {
        var name = (host ?? string.Empty).Trim().TrimEnd('.').Trim('[', ']').ToLowerInvariant();
        if (name.Length == 0)
        {
            return true;
        }

        if (IPAddress.TryParse(name, out var literal))
        {
            return !IsPublic(literal);
        }

        return name == "localhost" || !name.Contains('.', StringComparison.Ordinal) || name.EndsWith(".localhost", StringComparison.Ordinal)
            || name.EndsWith(".local", StringComparison.Ordinal) || name.EndsWith(".internal", StringComparison.Ordinal)
            || name.EndsWith(".home.arpa", StringComparison.Ordinal);
    }
}
