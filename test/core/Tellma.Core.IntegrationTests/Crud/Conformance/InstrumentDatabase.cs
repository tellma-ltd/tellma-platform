// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Data;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The databases of <see cref="ConformanceDatabase" />, served by an instance whose large-batch
    ///     threshold is a handful of rows, so a test drives the instruments' large lane with a payload
    ///     of a few rows rather than thousands.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class InstrumentDatabase(SqlServerFixture sql) : ConformanceDatabase(sql)
    {
        /// <summary>The most rows an operation of the instance takes in its small lane.</summary>
        public const int LargeBatchThreshold = 2;

        /// <inheritdoc />
        protected override void Configure(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _ = services.PostConfigure<DataOptions>(static options => options.LargeBatchThreshold = LargeBatchThreshold);
        }
    }
}
