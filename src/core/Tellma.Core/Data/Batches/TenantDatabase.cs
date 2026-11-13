// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The scope's tenant database: batches on fresh connections from the scope's connection
    ///     provider, which binds the scope's tenant. The schema and context values are read from the
    ///     request context when asked for.
    /// </summary>
    /// <param name="executor">The executor.</param>
    /// <param name="services">The scope.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="connections">The scope's connection provider.</param>
    internal sealed class TenantDatabase(
        DataExecutor executor, IServiceProvider services, IRequestContextAccessor accessor, ITenantConnectionProvider connections)
        : ITenantDatabase, IBatchConnection
    {
        /// <inheritdoc />
        public QueryexSchema Schema
        {
            get
            {
                IQueryexSchemaProvider provider = services.GetService<IQueryexSchemaProvider>()
                    ?? throw new InvalidOperationException("No query schema provider is composed, so the tenant database has no schema.");
                return provider.GetSchema(Shape);
            }
        }

        /// <inheritdoc />
        public QueryexContextValues Context
        {
            get
            {
                RequestContext context = accessor.Current;
                TenantSettings settings = Settings();
                return new QueryexContextValues(
                    context.Today, TimeZoneInfo.ConvertTime(context.Now, settings.TimeZone), context.UserId, settings.SqlServerTimeZoneName);
            }
        }

        /// <inheritdoc />
        public MultilingualShape Shape => Settings().Shape;

        /// <inheritdoc />
        public int TenantId => accessor.Current.RequireTenant().Id;

        /// <inheritdoc />
        public int? UserId => accessor.Current.UserId;

        /// <inheritdoc />
        public IDataBatch CreateBatch(BatchPurpose purpose)
        {
            return new DataBatch(executor, this, services, purpose);
        }

        /// <inheritdoc />
        public async ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
        {
            return await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask ReleaseAsync(SqlConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);

            // Back to the pool.
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>The scope's tenant settings.</summary>
        /// <returns>The settings.</returns>
        /// <exception cref="InvalidOperationException">They are not loaded yet.</exception>
        private TenantSettings Settings()
        {
            return accessor.Current.TenantSettings ?? throw new InvalidOperationException(
                "The tenant's settings are not loaded in this scope yet, so the tenant database has no schema or context values. Read them after the scope's settings load.");
        }
    }
}
