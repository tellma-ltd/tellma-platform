// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Testing.Database;
using Tellma.Identity.Data;
using Tellma.Identity.Migrations;

namespace Tellma.Identity.Tests.Store
{
    /// <summary>
    ///     The identity store's migrations, checked without a database: the model carries the
    ///     platform collation and no migration changes a database's collation, and the constant the
    ///     store check reads names the last migration.
    /// </summary>
    public sealed class IdentityStoreMigrationsTests
    {
        [Fact]
        public void The_identity_migrations_hold_the_platform_collation()
        {
            using TellmaIdentityDbContext context = new TellmaIdentityDesignTimeDbContextFactory().CreateDbContext([]);

            CollationPin.Verify(context);
        }

        [Fact]
        public void The_published_latest_migration_is_the_last_migration_in_the_assembly()
        {
            using TellmaIdentityDbContext context = new TellmaIdentityDesignTimeDbContextFactory().CreateDbContext([]);

            LatestMigrationPin.Verify(context, TellmaIdentityConstants.LatestMigration);
        }
    }
}
