// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The grammar of a tenant location's parts and the allowed-servers rule: which server names,
    ///     database names and profile names a catalog row may hold, and which hosts a row may send the
    ///     application's credential to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A server is <c>[tcp:]host[\instance][,port]</c>. The host is <c>(localdb)</c> or a
    ///         name of letters, digits, <c>.</c>, <c>-</c> and <c>_</c>; an instance name is letters,
    ///         digits, <c>_</c>, <c>$</c> and <c>#</c>; a port is a number from 1 to 65535. Anything else
    ///         (another protocol prefix, a named pipe, white space, an IPv6 literal) is refused, so the
    ///         host part the allowed-servers rule compares is the host SqlClient connects to.
    ///     </para>
    ///     <para>
    ///         An allowed-servers entry starting with <c>.</c> matches any host ending with it and
    ///         longer than it; any other entry matches one host. Both comparisons ignore case.
    ///     </para>
    /// </remarks>
    internal static class TenantLocations
    {
        /// <summary>The longest server value a row may hold.</summary>
        public const int MaxServerLength = 255;

        /// <summary>The longest database name SQL Server accepts.</summary>
        public const int MaxDatabaseLength = 128;

        /// <summary>The longest profile name a row may hold.</summary>
        public const int MaxProfileLength = 64;

        /// <summary>SQL Server's default TCP port, which a canonical server leaves out.</summary>
        public const int DefaultPort = 1433;

        /// <summary>The protocol prefix the grammar accepts.</summary>
        private const string TcpPrefix = "tcp:";

        /// <summary>The LocalDB pseudo-host.</summary>
        private const string LocalDbHost = "(localdb)";

        /// <summary>Parses a server value and returns its host part.</summary>
        /// <param name="server">The server value of a row.</param>
        /// <param name="host">The host part, when the value parses.</param>
        /// <returns>True when the value follows the grammar.</returns>
        public static bool TryParseServer(string? server, out string host)
        {
            host = string.Empty;
            if (string.IsNullOrEmpty(server) || server.Length > MaxServerLength)
            {
                return false;
            }

            ReadOnlySpan<char> rest = server.AsSpan();
            if (rest.StartsWith(TcpPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[TcpPrefix.Length..];
            }

            // --- ,port ---
            int comma = rest.IndexOf(',');
            if (comma >= 0)
            {
                if (!IsPort(rest[(comma + 1)..]))
                {
                    return false;
                }

                rest = rest[..comma];
            }

            // --- \instance ---
            int backslash = rest.IndexOf('\\');
            if (backslash >= 0)
            {
                if (!IsInstance(rest[(backslash + 1)..]))
                {
                    return false;
                }

                rest = rest[..backslash];
            }

            // --- host ---
            if (!IsHost(rest))
            {
                return false;
            }

            host = rest.ToString();
            return true;
        }

        /// <summary>
        ///     The canonical spelling of a server, which a row stores so that two spellings of one
        ///     server compare equal: no <c>tcp:</c> prefix, no default port <c>,1433</c>, lower case
        ///     (SQL Server host and instance names ignore case). Two names DNS resolves alike, such as a
        ///     short name and its fully qualified one, are not unified.
        /// </summary>
        /// <param name="server">The server value; it must follow the grammar.</param>
        /// <returns>The canonical value.</returns>
        /// <exception cref="ArgumentException">The value does not follow the grammar.</exception>
        public static string Canonicalize(string server)
        {
            if (!TryParseServer(server, out _))
            {
                throw new ArgumentException("The server does not follow the location grammar.", nameof(server));
            }

            ReadOnlySpan<char> rest = server.AsSpan();
            if (rest.StartsWith(TcpPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[TcpPrefix.Length..];
            }

            string canonical = rest.ToString().ToLowerInvariant();
            int comma = canonical.IndexOf(',', StringComparison.Ordinal);
            return comma >= 0 && int.Parse(canonical.AsSpan(comma + 1), System.Globalization.CultureInfo.InvariantCulture) == DefaultPort
                ? canonical[..comma]
                : canonical;
        }

        /// <summary>Whether a server value parses and its host is allowed.</summary>
        /// <param name="server">The server value.</param>
        /// <param name="allowedServers">The allowed-servers entries.</param>
        /// <param name="isDevelopment">Whether the host runs in Development, where an empty list allows every host.</param>
        /// <returns>True when a row may name the server.</returns>
        public static bool IsServerAllowed(string? server, IEnumerable<string> allowedServers, bool isDevelopment)
        {
            ArgumentNullException.ThrowIfNull(allowedServers);
            if (!TryParseServer(server, out string host))
            {
                return false;
            }

            bool any = false;
            foreach (string entry in allowedServers)
            {
                any = true;
                if (Matches(host, entry))
                {
                    return true;
                }
            }

            // An empty list allows every host only in Development; outside it the list is required.
            return !any && isDevelopment;
        }

        /// <summary>Whether an allowed-servers entry is well formed: a host, or <c>.</c> followed by a host.</summary>
        /// <param name="entry">The entry.</param>
        /// <returns>True when it is.</returns>
        public static bool IsValidAllowedEntry(string? entry)
        {
            return !string.IsNullOrEmpty(entry)
                && (entry[0] == '.' ? entry.Length > 1 && IsHost(entry.AsSpan(1)) : IsHost(entry));
        }

        /// <summary>Whether a database name is one a row may hold: 1 to 128 characters, no control character, <c>;</c>, <c>=</c> or <c>]</c>.</summary>
        /// <param name="database">The name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsValidDatabaseName(string? database)
        {
            if (string.IsNullOrWhiteSpace(database) || database.Length > MaxDatabaseLength || database.Trim().Length != database.Length)
            {
                return false;
            }

            foreach (char c in database)
            {
                if (char.IsControl(c) || c is ';' or '=' or ']')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a profile name is one a row may hold: 1 to 64 letters, digits, <c>.</c>, <c>-</c> or <c>_</c>.</summary>
        /// <param name="profile">The name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsValidProfileName(string? profile)
        {
            if (string.IsNullOrEmpty(profile) || profile.Length > MaxProfileLength)
            {
                return false;
            }

            foreach (char c in profile)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a host matches one allowed-servers entry.</summary>
        /// <param name="host">The host part of a server.</param>
        /// <param name="entry">The entry.</param>
        /// <returns>True when it matches.</returns>
        private static bool Matches(string host, string entry)
        {
            if (string.IsNullOrEmpty(entry))
            {
                return false;
            }

            // A suffix needs at least one character before it, so '.database.windows.net' never
            // matches the bare suffix itself.
            return entry[0] == '.'
                ? host.Length > entry.Length && host.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
                : string.Equals(host, entry, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a span is a host: <c>(localdb)</c>, or letters, digits, <c>.</c>, <c>-</c> and <c>_</c>.</summary>
        /// <param name="value">The span.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHost(ReadOnlySpan<char> value)
        {
            if (value.Equals(LocalDbHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.IsEmpty)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a span is a SQL Server instance name.</summary>
        /// <param name="value">The span.</param>
        /// <returns>True when it is.</returns>
        private static bool IsInstance(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty || value.Length > 128)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '$' or '#'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a span is a TCP port, 1 to 65535.</summary>
        /// <param name="value">The span.</param>
        /// <returns>True when it is.</returns>
        private static bool IsPort(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty || value.Length > 5)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            int port = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            return port is >= 1 and <= 65535;
        }
    }
}
