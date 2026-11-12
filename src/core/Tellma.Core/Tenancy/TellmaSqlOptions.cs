// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     How tenant databases are reached and named, bound from <c>Tellma:Sql</c>: the credential
    ///     profiles a tenant row selects, the pool bounds, the servers a tenant row may name, and the
    ///     naming and creation of provisioned databases.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The catalog holds no connection string: a tenant row names a server, a database and a
    ///         profile, and the registry composes the tenant's connection string from the profile's
    ///         fragment and the row. The <c>core.sql-profiles</c> startup check enforces the profile
    ///         rules: a <c>default</c> profile, <c>Max Pool Size</c> set within
    ///         <see cref="MaxPoolSizePerTenant" />, encryption and no password outside Development,
    ///         and <see cref="AllowedServers" /> set outside Development.
    ///     </para>
    ///     <para>A profile may carry a credential, so no message ever quotes a profile's value.</para>
    /// </remarks>
    public sealed class TellmaSqlOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Sql";

        /// <summary>The profile a tenant registration selects unless it names another.</summary>
        public const string DefaultProfile = "default";

        /// <summary>The longest database prefix accepted, so <c>&lt;prefix&gt;.&lt;id&gt;</c> fits a database name.</summary>
        public const int MaxDatabasePrefixLength = 100;

        /// <summary>The longest application principal: a database user's name is a <c>sysname</c>.</summary>
        public const int MaxPrincipalLength = 128;

        /// <summary>
        ///     The credential profiles by name, compared without regard to case: SqlClient
        ///     connection-string fragments holding the authentication, encryption and pool settings a
        ///     tenant's connections use. The row's server and database, and the deployment's
        ///     application name, are composed over the fragment.
        /// </summary>
        public Dictionary<string, string> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The highest <c>Max Pool Size</c> a profile may set; 20 by default.</summary>
        [Range(1, 32767)]
        public int MaxPoolSizePerTenant { get; set; } = 20;

        /// <summary>
        ///     How many tenants may use profiles that keep warm connections (a non-zero
        ///     <c>Min Pool Size</c>) before the registry warns; 10 by default.
        /// </summary>
        [Range(0, int.MaxValue)]
        public int MaxWarmTenants { get; set; } = 10;

        /// <summary>Whether a profile may carry a SQL password outside Development; false by default.</summary>
        public bool AllowSqlPassword { get; set; }

        /// <summary>
        ///     The prefix of provisioned databases, named <c>&lt;prefix&gt;.&lt;id&gt;</c>, and of the
        ///     Development catalog, <c>&lt;prefix&gt;.catalog</c>; <c>Tellma.dev.&lt;slug&gt;</c> when
        ///     unset.
        /// </summary>
        public string? DatabasePrefix { get; set; }

        /// <summary>
        ///     The web application's database principal, which provisioning creates a user for and
        ///     adds to the application role; empty in Development, where the host connects as the
        ///     database owner.
        /// </summary>
        public string? ApplicationPrincipal { get; set; }

        /// <summary>
        ///     The hosts a tenant row's server may name, compared without regard to case: an entry
        ///     starting with <c>.</c> matches any host ending with it (<c>.database.windows.net</c>),
        ///     any other entry one host. Required outside Development; in Development an empty list
        ///     allows every host.
        /// </summary>
        public IList<string> AllowedServers { get; } = [];

        /// <summary>
        ///     The options the migrator appends after <c>COLLATE</c> when it creates a database on
        ///     Azure SQL Database, such as <c>(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))</c>;
        ///     null on-premises.
        /// </summary>
        public string? CreateDatabaseOptions { get; set; }

        /// <summary>The database prefix in effect: <see cref="DatabasePrefix" />, else <c>Tellma.dev.&lt;slug&gt;</c>.</summary>
        /// <param name="slug">The distribution slug.</param>
        /// <returns>The prefix.</returns>
        public string EffectiveDatabasePrefix(string slug)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            return string.IsNullOrWhiteSpace(DatabasePrefix) ? $"Tellma.dev.{slug}" : DatabasePrefix;
        }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // The prefix becomes part of every provisioned database's name, which a catalog row's
            // CHECK constraint keeps free of ';' and '='.
            if (DatabasePrefix is { Length: > 0 } prefix
                && (prefix.Length > MaxDatabasePrefixLength || !TenantLocations.IsValidDatabaseName(prefix)))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(DatabasePrefix)} is '{prefix}'; it must be at most {MaxDatabasePrefixLength} printable characters without ';', '=', ']' or a control character.",
                    [nameof(DatabasePrefix)]);
            }

            // The options are appended to a CREATE DATABASE statement, so they may not end it early.
            if (CreateDatabaseOptions is { } createOptions
                && (createOptions.Contains(';', StringComparison.Ordinal)
                    || createOptions.Contains("--", StringComparison.Ordinal)
                    || createOptions.Contains("/*", StringComparison.Ordinal)))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(CreateDatabaseOptions)} holds ';' or a comment; it must be a single clause such as '(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))'.",
                    [nameof(CreateDatabaseOptions)]);
            }

            // The principal names a database user the migrator creates and adds to the application
            // role, so it can be neither the role itself nor a name a bracketed identifier cannot hold.
            if (ApplicationPrincipal is { Length: > 0 } principal)
            {
                if (string.Equals(principal, DatabaseRoles.Application, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new ValidationResult(
                        $"{SectionPath}:{nameof(ApplicationPrincipal)} is '{principal}', the application role's own name; it names the web application's login or managed identity, which the role gets as a member.",
                        [nameof(ApplicationPrincipal)]);
                }
                else if (principal.Length > MaxPrincipalLength || string.IsNullOrWhiteSpace(principal) || principal.Any(char.IsControl))
                {
                    yield return new ValidationResult(
                        $"{SectionPath}:{nameof(ApplicationPrincipal)} must be 1 to {MaxPrincipalLength} characters without a control character.",
                        [nameof(ApplicationPrincipal)]);
                }
            }
        }
    }
}
