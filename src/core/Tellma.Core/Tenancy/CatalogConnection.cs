// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog's connection: the configured string with the deployment's application name,
    ///     composed once, and the server it names, the default server of a registration.
    /// </summary>
    /// <param name="options">The catalog options.</param>
    /// <param name="deployment">The running deployment.</param>
    internal sealed class CatalogConnection(IOptions<TellmaCatalogOptions> options, DeploymentIdentity deployment)
    {
        private readonly Lazy<SqlConnectionStringBuilder> _builder = new(() =>
        {
            // Unset or malformed options are the startup gate's to report; a caller that gets here
            // regardless learns which key to set, never the value.
            string connectionString = options.Value.ConnectionString is { Length: > 0 } configured && TellmaCatalogOptions.Parses(configured)
                ? configured
                : throw new InvalidOperationException($"{TellmaCatalogOptions.SectionPath}:{nameof(TellmaCatalogOptions.ConnectionString)} is not set to a valid connection string.");

            return new SqlConnectionStringBuilder(TenantConnectionStrings.ComposeCatalog(connectionString, deployment.DeploymentId));
        });

        /// <summary>The composed catalog connection string; never log it.</summary>
        /// <exception cref="InvalidOperationException">The catalog connection string is not configured.</exception>
        public string ConnectionString => _builder.Value.ConnectionString;

        /// <summary>The server the catalog connection names, which a registration without a location defaults to.</summary>
        /// <exception cref="InvalidOperationException">The catalog connection string is not configured.</exception>
        public string Server => _builder.Value.DataSource;

        /// <summary>Opens a connection to the catalog.</summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        /// <exception cref="InvalidOperationException">The catalog connection string is not configured.</exception>
        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            return OpenAsync(ConnectionString, cancellationToken);
        }

        /// <summary>
        ///     Opens a connection to the catalog without SqlClient's retry of a transient login
        ///     failure, so a catalog database that does not exist is reported at once rather than after
        ///     the retry interval. The startup check of the catalog's migrations opens this one.
        /// </summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        /// <exception cref="InvalidOperationException">The catalog connection string is not configured.</exception>
        public Task<SqlConnection> OpenWithoutRetryAsync(CancellationToken cancellationToken)
        {
            // A string of its own, so a pool of its own; the check runs once per startup.
            SqlConnectionStringBuilder builder = new(ConnectionString) { ConnectRetryCount = 0 };
            return OpenAsync(builder.ConnectionString, cancellationToken);
        }

        /// <summary>Opens a connection over a composed catalog string.</summary>
        /// <param name="connectionString">The string.</param>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        private static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
        {
            SqlConnection connection = new(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
