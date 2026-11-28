// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Sockets;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Decides which resolved addresses a metadata fetch may connect to: only globally
    ///     reachable unicast ones. Loopback, link-local, private, multicast, and every other
    ///     special-purpose range that is not globally reachable are refused, so a document URL
    ///     can never make the server reach into its own network, its cloud metadata endpoint or
    ///     itself.
    /// </summary>
    public static class ClientMetadataAddressPolicy
    {
        /// <summary>
        ///     The IPv4 ranges refused (the IANA special-purpose registry's ranges that are not
        ///     globally reachable, plus multicast and the reserved block).
        /// </summary>
        private static readonly (uint Network, int PrefixLength)[] RefusedIPv4 =
        [
            (0x00000000, 8),   // 0.0.0.0/8: "this network"
            (0x0A000000, 8),   // 10.0.0.0/8: private
            (0x64400000, 10),  // 100.64.0.0/10: shared address space (carrier-grade NAT)
            (0x7F000000, 8),   // 127.0.0.0/8: loopback
            (0xA9FE0000, 16),  // 169.254.0.0/16: link-local, which holds the cloud metadata endpoints
            (0xAC100000, 12),  // 172.16.0.0/12: private
            (0xC0000000, 24),  // 192.0.0.0/24: IETF protocol assignments
            (0xC0000200, 24),  // 192.0.2.0/24: documentation
            (0xC0586300, 24),  // 192.88.99.0/24: deprecated 6to4 relay anycast
            (0xC0A80000, 16),  // 192.168.0.0/16: private
            (0xC6120000, 15),  // 198.18.0.0/15: benchmarking
            (0xC6336400, 24),  // 198.51.100.0/24: documentation
            (0xCB007100, 24),  // 203.0.113.0/24: documentation
            (0xE0000000, 4),   // 224.0.0.0/4: multicast
            (0xF0000000, 4),   // 240.0.0.0/4: reserved, and the limited broadcast address
        ];

        /// <summary>Whether a fetch may connect to an address.</summary>
        /// <param name="address">A resolved address.</param>
        /// <returns>True only for a globally reachable unicast address.</returns>
        public static bool IsAllowed(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            // An IPv4-mapped IPv6 address reaches the IPv4 address it maps, so it is judged as one.
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            // Any other family is not an internet address at all.
            return address.AddressFamily is AddressFamily.InterNetwork
                ? IsAllowedIPv4(address)
                : address.AddressFamily is AddressFamily.InterNetworkV6 && IsAllowedIPv6(address);
        }

        /// <summary>Whether an IPv4 address lies outside every refused range.</summary>
        private static bool IsAllowedIPv4(IPAddress address)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!address.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            uint value = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            foreach ((uint network, int prefixLength) in RefusedIPv4)
            {
                uint mask = uint.MaxValue << (32 - prefixLength);
                if ((value & mask) == network)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Whether an IPv6 address is global unicast and embeds no refused IPv4 address. Only
        ///     the global unicast block (2000::/3) is accepted, minus its special-purpose ranges;
        ///     the translation prefixes that carry an IPv4 address are judged by that address.
        /// </summary>
        private static bool IsAllowedIPv6(IPAddress address)
        {
            Span<byte> bytes = stackalloc byte[16];
            if (!address.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            // Everything outside 2000::/3 — loopback, unspecified, IPv4-compatible, unique local
            // (fc00::/7), link-local (fe80::/10), site-local and multicast (ff00::/8) — is refused.
            if ((bytes[0] & 0xE0) != 0x20)
            {
                // The NAT64 well-known prefix (64:ff9b::/96) is the one translation prefix outside
                // 2000::/3; it reaches the IPv4 address in its last four bytes.
                return bytes[..12].SequenceEqual(Nat64Prefix) && IsAllowedIPv4(new IPAddress(bytes[12..]));
            }

            // 2001::/23 holds the IETF protocol assignments: Teredo (2001::/32), benchmarking
            // (2001:2::/48), ORCHID and the like. None is a public web host.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] < 0x02)
            {
                return false;
            }

            // 2001:db8::/32: documentation.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            {
                return false;
            }

            // 2002::/16: 6to4, which reaches the IPv4 address in bytes 2–5.
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsAllowedIPv4(new IPAddress(bytes[2..6]));
            }

            // 3fff::/20: documentation.
            return !(bytes[0] == 0x3F && bytes[1] == 0xFF && (bytes[2] & 0xF0) == 0x00);
        }

        /// <summary>The first twelve bytes of the NAT64 well-known prefix, 64:ff9b::/96.</summary>
        private static ReadOnlySpan<byte> Nat64Prefix => [0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0];
    }
}
