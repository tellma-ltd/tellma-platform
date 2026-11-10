// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Core.Tenancy
{
    /// <summary>One broken connection rule: what is wrong and how to fix it, naming a keyword, never a value.</summary>
    /// <param name="Problem">What is wrong.</param>
    /// <param name="Fix">The remedy.</param>
    internal sealed record SqlProfileViolation(string Problem, string Fix);

    /// <summary>
    ///     The connection rules every credential profile and the catalog connection follow: no
    ///     unresolved Key Vault reference, a valid fragment, <c>Max Pool Size</c> set within
    ///     <c>Tellma:Sql:MaxPoolSizePerTenant</c> (profiles only), and outside Development encryption
    ///     and no password unless <c>Tellma:Sql:AllowSqlPassword</c>. The startup check reports them,
    ///     and the registry applies them to every snapshot it composes, so a reloaded configuration
    ///     cannot put a tenant on a profile the startup check would refuse.
    /// </summary>
    internal static class SqlProfileRules
    {
        private const string KeyVaultLiteral = "@Microsoft.KeyVault(";

        /// <summary>Checks one fragment against the rules.</summary>
        /// <param name="fragment">The fragment.</param>
        /// <param name="subject">How a violation names it, as the start of a sentence.</param>
        /// <param name="key">The configuration key it comes from.</param>
        /// <param name="sql">The SQL options.</param>
        /// <param name="isDevelopment">Whether the host runs in Development.</param>
        /// <param name="requirePoolSize">Whether the fragment must set <c>Max Pool Size</c> within the per-tenant bound.</param>
        /// <returns>Every violation; empty when the fragment follows the rules.</returns>
        public static IReadOnlyList<SqlProfileViolation> Check(
            string fragment, string subject, string key, TellmaSqlOptions sql, bool isDevelopment, bool requirePoolSize)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            ArgumentNullException.ThrowIfNull(sql);

            // --- An unresolved Key Vault reference, anywhere in the value ---
            if (fragment.Contains(KeyVaultLiteral, StringComparison.OrdinalIgnoreCase))
            {
                return [new SqlProfileViolation(
                    $"{subject} holds an unresolved Key Vault reference.",
                    $"Grant the host's identity access to the vault so the reference in {key} resolves, or deliver the secret through a configuration provider.")];
            }

            if (!TenantConnectionStrings.TryParse(fragment, out SqlConnectionStringBuilder parsed))
            {
                return [new SqlProfileViolation(
                    $"{subject} is not a valid SqlClient connection string.",
                    $"Set {key} to keywords and values separated by ';', such as 'Encrypt=True;Max Pool Size=20'.")];
            }

            List<SqlProfileViolation> violations = [];

            // --- The pool bound: every profile sets it, within the per-tenant bound ---
            if (requirePoolSize && !parsed.ShouldSerialize("Max Pool Size"))
            {
                violations.Add(new SqlProfileViolation(
                    $"{subject} does not set Max Pool Size, so SqlClient's default of 100 per tenant would apply.",
                    $"Add 'Max Pool Size={sql.MaxPoolSizePerTenant}' (or less) to {key}."));
            }
            else if (requirePoolSize && parsed.MaxPoolSize > sql.MaxPoolSizePerTenant)
            {
                violations.Add(new SqlProfileViolation(
                    $"{subject} sets Max Pool Size to {parsed.MaxPoolSize}, above {TellmaSqlOptions.SectionPath}:MaxPoolSizePerTenant ({sql.MaxPoolSizePerTenant}).",
                    $"Lower Max Pool Size in {key} to {sql.MaxPoolSizePerTenant} or less, or raise {TellmaSqlOptions.SectionPath}:MaxPoolSizePerTenant with the server's session budget in mind."));
            }

            if (isDevelopment)
            {
                return violations;
            }

            // --- Outside Development: encryption, and no password unless allowed ---
            if (!parsed.ShouldSerialize("Encrypt") || parsed.Encrypt == SqlConnectionEncryptOption.Optional)
            {
                violations.Add(new SqlProfileViolation(
                    $"{subject} does not require encryption.",
                    $"Add 'Encrypt=True' (or 'Encrypt=Strict') to {key}."));
            }

            if (parsed.ShouldSerialize("Password") && !sql.AllowSqlPassword)
            {
                violations.Add(new SqlProfileViolation(
                    $"{subject} carries a SQL password outside Development.",
                    $"Connect with a managed identity or integrated security, or, where a SQL login is the only option, set {TellmaSqlOptions.SectionPath}:AllowSqlPassword to true and deliver the password through a configuration provider."));
            }

            return violations;
        }

        /// <summary>Whether a configured profile may carry tenant connections: a usable name and a fragment that follows every rule.</summary>
        /// <param name="name">The profile's name.</param>
        /// <param name="fragment">The profile's fragment.</param>
        /// <param name="sql">The SQL options.</param>
        /// <param name="isDevelopment">Whether the host runs in Development.</param>
        /// <returns>True when it may.</returns>
        public static bool IsUsable(string name, string fragment, TellmaSqlOptions sql, bool isDevelopment)
        {
            return TenantLocations.IsValidProfileName(name)
                && Check(fragment, name, name, sql, isDevelopment, requirePoolSize: true).Count == 0;
        }
    }
}
