// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Tellma.Core.Testing.Database
{
    /// <summary>
    ///     The latest-migration pin: a database-free check that a published constant naming a
    ///     migrations assembly's last migration still names it. A host that checks its store's
    ///     history against such a constant, instead of carrying the migrations, needs the constant
    ///     to move with every migration added.
    /// </summary>
    public static class LatestMigrationPin
    {
        /// <summary>Asserts that the constant names the last migration of the context's migrations assembly.</summary>
        /// <param name="context">A context built as the design-time factory builds it.</param>
        /// <param name="latestMigration">The published constant.</param>
        public static void Verify(DbContext context, string latestMigration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(latestMigration);

            string? last = LastMigrationId(context);
            if (!string.Equals(last, latestMigration, StringComparison.Ordinal))
            {
                Assert.Fail(
                    $"The published latest migration '{latestMigration}' is not the last migration of "
                        + $"{context.GetType().Name}'s migrations assembly, which is '{last ?? "(none)"}'. "
                        + "Point the constant at the migration just added.");
            }
        }

        /// <summary>The id of the last migration in the context's migrations assembly, in id order.</summary>
        /// <param name="context">A context built as the design-time factory builds it.</param>
        /// <returns>The id, or null when the assembly holds no migration.</returns>
        public static string? LastMigrationId(DbContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Migration ids lead with a UTC timestamp, so ordinal order is application order.
            return context.GetService<IMigrationsAssembly>().Migrations.Keys
                .Order(StringComparer.Ordinal)
                .LastOrDefault();
        }
    }
}
