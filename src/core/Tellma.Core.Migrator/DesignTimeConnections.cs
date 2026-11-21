// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     Where a design-time factory's connection string comes from: <c>--connection</c> among the
    ///     arguments <c>dotnet ef</c> passes through, else an environment variable, else a LocalDB
    ///     database of the factory's own.
    /// </summary>
    internal static class DesignTimeConnections
    {
        /// <summary>The argument that names the connection string.</summary>
        public const string ConnectionOption = "--connection";

        /// <summary>The LocalDB connection string of a design database.</summary>
        /// <param name="database">The database's name.</param>
        /// <returns>The connection string.</returns>
        public static string LocalDb(string database)
        {
            return $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        }

        /// <summary>Resolves the connection string of a design-time context.</summary>
        /// <param name="args">
        ///     The arguments after <c>--</c> on the <c>dotnet ef</c> command line: <c>--connection
        ///     &lt;value&gt;</c> or <c>--connection=&lt;value&gt;</c>.
        /// </param>
        /// <param name="variable">The environment variable consulted next.</param>
        /// <param name="fallback">The connection string used when neither names one.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ArgumentException"><c>--connection</c> is given without a value.</exception>
        public static string Resolve(IReadOnlyList<string>? args, string variable, string fallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable);
            ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

            // --- The argument, in either spelling; the last one wins, as on any command line ---
            string? fromArguments = null;
            for (int index = 0; args is not null && index < args.Count; index++)
            {
                string argument = args[index];
                if (string.Equals(argument, ConnectionOption, StringComparison.Ordinal))
                {
                    fromArguments = index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1])
                        ? args[++index]
                        : throw new ArgumentException($"{ConnectionOption} needs a connection string after it.", nameof(args));
                }
                else if (argument.StartsWith(ConnectionOption + "=", StringComparison.Ordinal))
                {
                    string value = argument[(ConnectionOption.Length + 1)..];
                    fromArguments = !string.IsNullOrWhiteSpace(value)
                        ? value
                        : throw new ArgumentException($"{ConnectionOption} needs a connection string after it.", nameof(args));
                }
            }

            // --- Then the variable, then the fallback ---
            return fromArguments
                ?? (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured && !string.IsNullOrWhiteSpace(configured) ? configured : fallback);
        }
    }
}
