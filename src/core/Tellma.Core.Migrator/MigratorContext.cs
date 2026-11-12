// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Tellma.Core.Composition;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     What every command of one run works with: the started host's services, the composition it
    ///     was built from, the tenant migrations' assembly, the identity store's migration, and the
    ///     report's writer.
    /// </summary>
    /// <param name="services">The started host's root services.</param>
    /// <param name="slug">The distribution slug.</param>
    /// <param name="compose">The distribution's composition method.</param>
    /// <param name="migrateIdentityStore">Migrates the in-proc identity store over the catalog's connection string, or null.</param>
    /// <param name="migrationsAssembly">The assembly that carries the tenant model's migrations.</param>
    /// <param name="output">Where the report is written.</param>
    internal sealed class MigratorContext(
        IServiceProvider services,
        string slug,
        Action<TellmaBuilder> compose,
        Func<string, Task>? migrateIdentityStore,
        Assembly migrationsAssembly,
        TextWriter output)
    {
        /// <summary>The configuration key of the identity mode.</summary>
        public const string IdentityModeKey = "Tellma:Identity:Mode";

        /// <summary>The identity mode that keeps the identity store in the catalog.</summary>
        public const string InProcIdentityMode = "InProc";

        /// <summary>The configuration key of the seed tenants.</summary>
        public const string SeedTenantsKey = "Tellma:Seed:Tenants";

        /// <summary>The configuration key of the Development administrator's email address.</summary>
        public const string SeedAdminEmailKey = "Tellma:Seed:AdminEmail";

        /// <summary>The fixed subject of the Development administrator a seeded email address names.</summary>
        public const string DevelopmentAdminSubject = "00000000-0000-0000-0000-000000000001";

        /// <summary>The actor every catalog write of the migrator names.</summary>
        public const string Actor = "migrator";

        private readonly Lock _outputLock = new();

        /// <summary>The started host's root services.</summary>
        public IServiceProvider Services { get; } = services;

        /// <summary>The distribution slug.</summary>
        public string Slug { get; } = slug;

        /// <summary>The distribution's composition method.</summary>
        public Action<TellmaBuilder> Compose { get; } = compose;

        /// <summary>Migrates the in-proc identity store over the catalog's connection string, or null.</summary>
        public Func<string, Task>? MigrateIdentityStore { get; } = migrateIdentityStore;

        /// <summary>The assembly that carries the tenant model's migrations.</summary>
        public Assembly MigrationsAssembly { get; } = migrationsAssembly;

        /// <summary>The host's configuration.</summary>
        public IConfiguration Configuration => Services.GetRequiredService<IConfiguration>();

        /// <summary>The host's environment.</summary>
        public IHostEnvironment Environment => Services.GetRequiredService<IHostEnvironment>();

        /// <summary>Whether the identity store lives in the catalog.</summary>
        public bool InProcIdentity => IsInProcIdentity(Configuration);

        /// <summary>The migrator's logger.</summary>
        public ILogger Logger => Services.GetRequiredService<ILoggerFactory>().CreateLogger(MigratorLog.Category);

        /// <summary>Whether a configuration keeps the identity store in the catalog.</summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>True in in-proc identity mode.</returns>
        public static bool IsInProcIdentity(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return string.Equals(configuration[IdentityModeKey], InProcIdentityMode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Writes one line of the report.</summary>
        /// <param name="line">The line.</param>
        public void WriteLine(string line)
        {
            lock (_outputLock)
            {
                output.WriteLine(line);
            }
        }

        /// <summary>
        ///     The administrator a provisioning names: the given email address, else the seeded one in
        ///     Development; the given subject in Development, else the fixed development subject when
        ///     the seeded email address supplied the email.
        /// </summary>
        /// <param name="email">The <c>--admin-email</c> value, or null.</param>
        /// <param name="subject">The <c>--admin-subject</c> value, or null; refused outside Development before this is called.</param>
        /// <returns>The administrator.</returns>
        public ProvisioningAdministrator Administrator(string? email, string? subject)
        {
            if (!Environment.IsDevelopment())
            {
                return new ProvisioningAdministrator(email, null);
            }

            string? seeded = Configuration[SeedAdminEmailKey] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : null;
            return email is not null
                ? new ProvisioningAdministrator(email, subject)
                : new ProvisioningAdministrator(seeded, subject ?? (seeded is null ? null : DevelopmentAdminSubject));
        }
    }

    /// <summary>The first administrator a provisioning names.</summary>
    /// <param name="Email">The email address, or null when the run names none.</param>
    /// <param name="Subject">The identity subject, in Development only.</param>
    internal sealed record ProvisioningAdministrator(string? Email, string? Subject);
}
