// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.sql-profiles</c>: the credential profiles tenant connections are composed from, and
    ///     the catalog connection, follow the platform's connection rules, and the allowed servers are
    ///     set.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>default</c> profile exists; every profile is a valid fragment that sets
    ///         <c>Max Pool Size</c> within <c>Tellma:Sql:MaxPoolSizePerTenant</c>; outside Development
    ///         every profile and the catalog connection encrypt and carry no SQL password unless
    ///         <c>Tellma:Sql:AllowSqlPassword</c> is set; no value holds an unresolved Key Vault
    ///         reference; and outside Development <c>Tellma:Sql:AllowedServers</c> lists at least one
    ///         well-formed entry.
    ///     </para>
    ///     <para>
    ///         Problems name a profile and a keyword, never a value: a profile may carry a credential.
    ///         Options that fail their own validation are left to <c>core.options</c>.
    ///     </para>
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class SqlProfilesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        private const string ProfilesKey = TellmaSqlOptions.SectionPath + ":Profiles";
        private const string AllowedServersKey = TellmaSqlOptions.SectionPath + ":AllowedServers";
        private const string CatalogKey = TellmaCatalogOptions.SectionPath + ":ConnectionString";

        /// <inheritdoc />
        public string Name => CoreCheckNames.SqlProfiles;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            bool isDevelopment = record.Environment.IsDevelopment();
            List<CompositionProblem> problems = [];

            // --- The profiles and the allowed servers ---
            if (Read<TellmaSqlOptions>(services) is TellmaSqlOptions sql)
            {
                CheckProfiles(sql, isDevelopment, problems);
                CheckAllowedServers(sql, isDevelopment, problems);

                // --- The catalog connection, under the same credential rules ---
                if (Read<TellmaCatalogOptions>(services)?.ConnectionString is { Length: > 0 } catalog)
                {
                    CheckFragment(catalog, $"The catalog connection ({CatalogKey})", CatalogKey, sql, isDevelopment, requirePoolSize: false, problems);
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>The bound options, or null when they fail their own validation, which <c>core.options</c> reports.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="services">The services.</param>
        /// <returns>The options, or null.</returns>
        private static TOptions? Read<TOptions>(IServiceProvider services)
            where TOptions : class
        {
            try
            {
                return services.GetRequiredService<IOptions<TOptions>>().Value;
            }
            catch (OptionsValidationException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                // The binder throws this for a value it cannot convert.
                return null;
            }
        }

        /// <summary>Checks the profiles: a default exists and every one follows the rules.</summary>
        /// <param name="sql">The SQL options.</param>
        /// <param name="isDevelopment">Whether the host runs in Development.</param>
        /// <param name="problems">Where problems are reported.</param>
        private void CheckProfiles(TellmaSqlOptions sql, bool isDevelopment, List<CompositionProblem> problems)
        {
            if (!sql.Profiles.ContainsKey(TellmaSqlOptions.DefaultProfile))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"No '{TellmaSqlOptions.DefaultProfile}' credential profile is configured, which every registration without its own location uses.",
                    $"Set {ProfilesKey}:{TellmaSqlOptions.DefaultProfile} to the connection-string fragment tenant connections use, such as 'Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20'."));
            }

            foreach ((string name, string fragment) in sql.Profiles.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                string key = $"{ProfilesKey}:{name}";
                if (!TenantLocations.IsValidProfileName(name))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The credential profile name '{name}' is not 1 to {TenantLocations.MaxProfileLength} letters, digits, '.', '-' or '_'.",
                        $"Rename {key} to a name a catalog row can hold, such as 'default' or 'warm'."));
                    continue;
                }

                CheckFragment(fragment, $"The credential profile '{name}'", key, sql, isDevelopment, requirePoolSize: true, problems);
            }
        }

        /// <summary>Checks one connection-string fragment against the rules.</summary>
        /// <param name="fragment">The fragment.</param>
        /// <param name="subject">How a problem names it, as the start of a sentence.</param>
        /// <param name="key">The configuration key it comes from.</param>
        /// <param name="sql">The SQL options.</param>
        /// <param name="isDevelopment">Whether the host runs in Development.</param>
        /// <param name="requirePoolSize">Whether the fragment must set <c>Max Pool Size</c> within the per-tenant bound.</param>
        /// <param name="problems">Where problems are reported.</param>
        private void CheckFragment(
            string fragment, string subject, string key, TellmaSqlOptions sql, bool isDevelopment, bool requirePoolSize, List<CompositionProblem> problems)
        {
            // The rules are shared with the registry, which applies them to every snapshot it composes.
            problems.AddRange(SqlProfileRules.Check(fragment, subject, key, sql, isDevelopment, requirePoolSize)
                .Select(violation => new CompositionProblem(Name, violation.Problem, violation.Fix)));
        }

        /// <summary>Checks the allowed servers: required outside Development, and every entry well formed.</summary>
        /// <param name="sql">The SQL options.</param>
        /// <param name="isDevelopment">Whether the host runs in Development.</param>
        /// <param name="problems">Where problems are reported.</param>
        private void CheckAllowedServers(TellmaSqlOptions sql, bool isDevelopment, List<CompositionProblem> problems)
        {
            if (sql.AllowedServers.Count == 0 && !isDevelopment)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{AllowedServersKey} is empty, so no tenant row could be served outside Development.",
                    $"List the hosts tenant databases live on in {AllowedServersKey}, such as '.database.windows.net' for every Azure SQL server."));
            }

            foreach (string entry in sql.AllowedServers)
            {
                if (!TenantLocations.IsValidAllowedEntry(entry))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The {AllowedServersKey} entry '{entry}' is neither a host nor '.' followed by a host suffix.",
                        $"Write each {AllowedServersKey} entry as a host name ('sql01.contoso.local') or a suffix starting with '.' ('.database.windows.net')."));
                }
            }
        }
    }
}
