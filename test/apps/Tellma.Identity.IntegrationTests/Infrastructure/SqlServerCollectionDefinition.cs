// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     The collection every database-backed test class joins to share one SQL Server. A
    ///     collection fixture rather than an assembly fixture, so the server starts only when such a
    ///     class runs and the protocol tests that never touch a database run without Docker. Each
    ///     test gets its own database: usually a name <see cref="SqlServerFixture.ReserveDatabase" />
    ///     reserves, which the engine's own startup creates through its model, or one
    ///     <see cref="SqlServerFixture.CreateDatabaseAsync" /> creates up front in a given collation.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerFixture>
    {
        /// <summary>The collection name used by <c>[Collection]</c> attributes.</summary>
        public const string Name = "SqlServer";
    }
}
