// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Composes connection strings with <see cref="SqlConnectionStringBuilder" />, never by
    ///     concatenation: a value is set as a value, so nothing a catalog row holds can add a keyword.
    /// </summary>
    internal static class TenantConnectionStrings
    {
        /// <summary>
        ///     A tenant's connection string: the profile's fragment, then <c>Data Source</c> from the
        ///     row's server, <c>Initial Catalog</c> from the row's database and <c>Application Name</c>
        ///     from the deployment. Every other keyword (the authentication, the credential, the
        ///     encryption, the pool bounds) is the profile's alone.
        /// </summary>
        /// <param name="profile">The profile's parsed fragment; not changed.</param>
        /// <param name="server">The row's server.</param>
        /// <param name="database">The row's database.</param>
        /// <param name="applicationName">The deployment id.</param>
        /// <returns>The composed string; never log it.</returns>
        public static string Compose(SqlConnectionStringBuilder profile, string server, string database, string applicationName)
        {
            ArgumentNullException.ThrowIfNull(profile);

            SqlConnectionStringBuilder builder = new(profile.ConnectionString)
            {
                DataSource = server,
                InitialCatalog = database,
                ApplicationName = applicationName,
            };

            return builder.ConnectionString;
        }

        /// <summary>The catalog's connection string with its <c>Application Name</c> set to the deployment id.</summary>
        /// <param name="connectionString">The configured catalog connection string.</param>
        /// <param name="applicationName">The deployment id.</param>
        /// <returns>The composed string; never log it.</returns>
        public static string ComposeCatalog(string connectionString, string applicationName)
        {
            SqlConnectionStringBuilder builder = new(connectionString)
            {
                ApplicationName = applicationName,
            };

            return builder.ConnectionString;
        }

        /// <summary>Parses a profile's fragment.</summary>
        /// <param name="fragment">The fragment.</param>
        /// <param name="profile">The parsed fragment, when it parses.</param>
        /// <returns>True when the fragment is a valid SqlClient connection string.</returns>
        public static bool TryParse(string? fragment, out SqlConnectionStringBuilder profile)
        {
            profile = null!;
            if (fragment is null || !TellmaCatalogOptions.Parses(fragment))
            {
                return false;
            }

            profile = new SqlConnectionStringBuilder(fragment);
            return true;
        }
    }
}
