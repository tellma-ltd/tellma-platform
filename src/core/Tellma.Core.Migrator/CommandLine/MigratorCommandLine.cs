// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Migrator.CommandLine
{
    /// <summary>One parsed migrator command.</summary>
    internal abstract record MigratorCommandLine
    {
        /// <summary>The usage text printed with every usage error.</summary>
        public const string Usage = """
            Usage:
              emit-client-assets <dir>
              migrate [--catalog-only] [--tenant <id>]... [--all-tenants] [--parallelism <n>]
              provision [--tenant <id>] [--name <name>] [--category Live|Sandbox] [--live-tenant <id>]
                        [--server <server>] [--database <database>] [--admin-email <email>] [--admin-subject <subject>]
              set-state --tenant <id> --state <state> [--reason <reason>]
              status
            """;

        /// <summary>The default bound on how many tenants <c>migrate</c> works on at once.</summary>
        public const int DefaultParallelism = 4;

        /// <summary>Parses the migrator's arguments.</summary>
        /// <param name="args">The arguments: a command, then its operands and options.</param>
        /// <returns>The parsed command.</returns>
        /// <exception cref="MigratorUsageException">The arguments do not form a command.</exception>
        public static MigratorCommandLine Parse(IReadOnlyList<string> args)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                throw new MigratorUsageException("No command was given.");
            }

            var options = Options.Read([.. args.Skip(1)]);
            return args[0] switch
            {
                "emit-client-assets" => ParseEmit(options),
                "migrate" => ParseMigrate(options),
                "provision" => ParseProvision(options),
                "set-state" => ParseSetState(options),
                "status" => ParseStatus(options),
                _ => throw new MigratorUsageException($"'{args[0]}' is not a migrator command."),
            };
        }

        /// <summary>Parses <c>emit-client-assets &lt;dir&gt;</c>.</summary>
        /// <param name="options">The arguments after the command.</param>
        /// <returns>The command.</returns>
        private static EmitClientAssetsCommandLine ParseEmit(Options options)
        {
            options.Allow();
            return options.Operands.Count == 1
                ? new EmitClientAssetsCommandLine(options.Operands[0])
                : throw new MigratorUsageException("emit-client-assets takes exactly one directory.");
        }

        /// <summary>Parses <c>migrate</c>.</summary>
        /// <param name="options">The arguments after the command.</param>
        /// <returns>The command.</returns>
        private static MigrateCommandLine ParseMigrate(Options options)
        {
            options.Allow("--catalog-only", "--tenant", "--all-tenants", "--parallelism");
            options.NoOperands("migrate");

            bool catalogOnly = options.Flag("--catalog-only");
            bool allTenants = options.Flag("--all-tenants");
            List<int> tenants = [.. options.All("--tenant").Select(static value => TenantId("--tenant", value))];
            int parallelism = options.Single("--parallelism") is string text ? Positive("--parallelism", text) : DefaultParallelism;

            // --- Contradictions ---
            string? contradiction =
                allTenants && tenants.Count > 0 ? "--all-tenants and --tenant cannot be combined."
                : catalogOnly && (allTenants || tenants.Count > 0) ? "--catalog-only migrates no tenant, so it takes neither --tenant nor --all-tenants."
                : null;

            return contradiction is null
                ? new MigrateCommandLine(catalogOnly, [.. tenants.Distinct()], parallelism)
                : throw new MigratorUsageException(contradiction);
        }

        /// <summary>Parses <c>provision</c>.</summary>
        /// <param name="options">The arguments after the command.</param>
        /// <returns>The command.</returns>
        private static ProvisionCommandLine ParseProvision(Options options)
        {
            options.Allow("--tenant", "--name", "--category", "--live-tenant", "--server", "--database", "--admin-email", "--admin-subject");
            options.NoOperands("provision");

            int? tenant = options.Single("--tenant") is string id ? TenantId("--tenant", id) : null;
            string? name = options.Single("--name");
            TenantCategory? category = options.Single("--category") is string text
                ? Enum.TryParse(text, ignoreCase: true, out TenantCategory parsed) && Enum.IsDefined(parsed) && !int.TryParse(text, out _)
                    ? parsed
                    : throw new MigratorUsageException($"--category is '{text}'; it is Live or Sandbox.")
                : null;
            int? liveTenant = options.Single("--live-tenant") is string live ? TenantId("--live-tenant", live) : null;
            string? server = options.Single("--server");
            string? database = options.Single("--database");

            // --- Registration or resumption ---
            string? contradiction =
                (name is null) != (category is null) ? "--name and --category register a tenant together; give both, or neither to resume one with --tenant."
                : name is null && tenant is null ? "provision registers a tenant with --name and --category, or resumes one with --tenant."
                : name is null && (liveTenant is not null || server is not null || database is not null) ? "--live-tenant, --server and --database describe a new registration, so they need --name and --category."
                : server is not null && database is null ? "--server needs --database: a tenant on another server names its database there."
                : null;

            return contradiction is null
                ? new ProvisionCommandLine(tenant, name, category, liveTenant, server, database, options.Single("--admin-email"), options.Single("--admin-subject"))
                : throw new MigratorUsageException(contradiction);
        }

        /// <summary>Parses <c>set-state</c>.</summary>
        /// <param name="options">The arguments after the command.</param>
        /// <returns>The command.</returns>
        private static SetStateCommandLine ParseSetState(Options options)
        {
            options.Allow("--tenant", "--state", "--reason");
            options.NoOperands("set-state");

            int tenant = options.Single("--tenant") is string id ? TenantId("--tenant", id) : throw new MigratorUsageException("set-state needs --tenant.");
            string state = options.Single("--state") ?? throw new MigratorUsageException("set-state needs --state.");
            return Enum.TryParse(state, ignoreCase: true, out TenantState parsed) && Enum.IsDefined(parsed) && !int.TryParse(state, out _)
                ? new SetStateCommandLine(tenant, parsed, options.Single("--reason"))
                : throw new MigratorUsageException($"--state is '{state}'; it is one of {string.Join(", ", Enum.GetNames<TenantState>())}.");
        }

        /// <summary>Parses <c>status</c>.</summary>
        /// <param name="options">The arguments after the command.</param>
        /// <returns>The command.</returns>
        private static StatusCommandLine ParseStatus(Options options)
        {
            options.Allow();
            options.NoOperands("status");
            return new StatusCommandLine();
        }

        /// <summary>A tenant id: a positive integer.</summary>
        /// <param name="option">The option it was given to.</param>
        /// <param name="value">The text.</param>
        /// <returns>The id.</returns>
        private static int TenantId(string option, string value)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
                ? id
                : throw new MigratorUsageException($"{option} is '{value}'; a tenant id is a positive integer.");
        }

        /// <summary>A positive integer.</summary>
        /// <param name="option">The option it was given to.</param>
        /// <param name="value">The text.</param>
        /// <returns>The number.</returns>
        private static int Positive(string option, string value)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
                ? number
                : throw new MigratorUsageException($"{option} is '{value}'; it is a positive integer.");
        }

        /// <summary>
        ///     The options and operands after a command. An option is <c>--name value</c> or
        ///     <c>--name=value</c>; a flag is <c>--name</c> alone, and is known by the command that
        ///     reads it.
        /// </summary>
        private sealed class Options
        {
            /// <summary>The options that take no value.</summary>
            private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--catalog-only", "--all-tenants" };

            private readonly List<(string Name, string? Value)> _options = [];

            /// <summary>The arguments that are not options, in order.</summary>
            public List<string> Operands { get; } = [];

            /// <summary>Splits the arguments into options and operands.</summary>
            /// <param name="args">The arguments after the command.</param>
            /// <returns>The options.</returns>
            public static Options Read(List<string> args)
            {
                Options options = new();
                for (int index = 0; index < args.Count; index++)
                {
                    string argument = args[index];
                    if (!argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        options.Operands.Add(argument);
                        continue;
                    }

                    // --- --name=value, a flag, or --name value ---
                    int equals = argument.IndexOf('=', StringComparison.Ordinal);
                    if (equals > 0)
                    {
                        options._options.Add((argument[..equals], argument[(equals + 1)..]));
                    }
                    else if (Flags.Contains(argument))
                    {
                        options._options.Add((argument, null));
                    }
                    else if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        options._options.Add((argument, args[++index]));
                    }
                    else
                    {
                        throw new MigratorUsageException($"{argument} needs a value.");
                    }
                }

                return options;
            }

            /// <summary>Refuses every option the command does not take.</summary>
            /// <param name="allowed">The options the command takes.</param>
            public void Allow(params string[] allowed)
            {
                foreach ((string name, _) in _options)
                {
                    if (!allowed.Contains(name, StringComparer.Ordinal))
                    {
                        throw new MigratorUsageException($"{name} is not an option of this command.");
                    }
                }
            }

            /// <summary>Refuses operands.</summary>
            /// <param name="command">The command, for the message.</param>
            public void NoOperands(string command)
            {
                if (Operands.Count > 0)
                {
                    throw new MigratorUsageException($"{command} takes no operand; '{Operands[0]}' was given.");
                }
            }

            /// <summary>Whether a flag is given; a flag given a value is refused.</summary>
            /// <param name="name">The flag.</param>
            /// <returns>True when given.</returns>
            public bool Flag(string name)
            {
                List<(string Name, string? Value)> given = [.. _options.Where(option => option.Name == name)];
                return given.Any(static option => option.Value is not null)
                    ? throw new MigratorUsageException($"{name} takes no value.")
                    : given.Count > 0;
            }

            /// <summary>The value of an option given at most once.</summary>
            /// <param name="name">The option.</param>
            /// <returns>The value, or null when not given.</returns>
            public string? Single(string name)
            {
                List<string> values = All(name);
                return values.Count switch
                {
                    0 => null,
                    1 => values[0],
                    _ => throw new MigratorUsageException($"{name} is given more than once."),
                };
            }

            /// <summary>Every value of an option, in order; a blank value is refused.</summary>
            /// <param name="name">The option.</param>
            /// <returns>The values.</returns>
            public List<string> All(string name)
            {
                List<string> values = [];
                foreach ((string option, string? value) in _options.Where(option => option.Name == name))
                {
                    values.Add(!string.IsNullOrWhiteSpace(value) ? value : throw new MigratorUsageException($"{option} needs a value."));
                }

                return values;
            }
        }
    }

    /// <summary><c>emit-client-assets &lt;dir&gt;</c>.</summary>
    /// <param name="Directory">The generated folder the files are written into.</param>
    internal sealed record EmitClientAssetsCommandLine(string Directory) : MigratorCommandLine;

    /// <summary><c>migrate</c>.</summary>
    /// <param name="CatalogOnly">Whether only the catalog is migrated.</param>
    /// <param name="Tenants">The tenants named; empty for every one.</param>
    /// <param name="Parallelism">How many tenants are worked on at once.</param>
    internal sealed record MigrateCommandLine(bool CatalogOnly, IReadOnlyList<int> Tenants, int Parallelism) : MigratorCommandLine;

    /// <summary><c>provision</c>.</summary>
    /// <param name="Tenant">The tenant to resume, or the id a registration requests.</param>
    /// <param name="Name">The new tenant's name, or null to resume.</param>
    /// <param name="Category">The new tenant's category, or null to resume.</param>
    /// <param name="LiveTenant">The live tenant a new sandbox derives from.</param>
    /// <param name="Server">The new tenant's server, or null for the catalog's.</param>
    /// <param name="Database">The new tenant's database, or null for the default name.</param>
    /// <param name="AdminEmail">The first administrator's email address.</param>
    /// <param name="AdminSubject">The first administrator's subject, in Development only.</param>
    internal sealed record ProvisionCommandLine(
        int? Tenant,
        string? Name,
        TenantCategory? Category,
        int? LiveTenant,
        string? Server,
        string? Database,
        string? AdminEmail,
        string? AdminSubject) : MigratorCommandLine;

    /// <summary><c>set-state</c>.</summary>
    /// <param name="Tenant">The tenant.</param>
    /// <param name="State">The state to move it to.</param>
    /// <param name="Reason">Why, or null.</param>
    internal sealed record SetStateCommandLine(int Tenant, TenantState State, string? Reason) : MigratorCommandLine;

    /// <summary><c>status</c>.</summary>
    internal sealed record StatusCommandLine : MigratorCommandLine;
}
