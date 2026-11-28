// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.Abstractions.Hosting;
using Xunit;

namespace Tellma.Core.Testing.Database
{
    /// <summary>
    ///     The collation pin: a database-free check, run over one migrations assembly and its model,
    ///     that the model declares the platform collation and that no migration ever changes a
    ///     database's collation. Every suite that owns a migrations assembly runs it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A database's collation is set once, by <c>CREATE DATABASE</c>. A model whose collation
    ///         changed would scaffold an <c>ALTER DATABASE … COLLATE</c>, which Azure SQL Database
    ///         refuses and which on SQL Server leaves every existing column on the old collation, so
    ///         the pin refuses such a migration before it can ship.
    ///     </para>
    ///     <para>
    ///         It asserts three things: the design-time model answers <c>GetCollation()</c> with the
    ///         expected collation; the model has no changes the migrations do not capture (so the
    ///         last snapshot carries the collation too); and no migration's up or down operations
    ///         hold an <see cref="AlterDatabaseOperation" /> whose collation differs from its old
    ///         collation. An initial migration's operation that carries only annotations passes.
    ///     </para>
    /// </remarks>
    public static class CollationPin
    {
        /// <summary>
        ///     Asserts the pin over the context's model and the migrations assembly it is configured
        ///     with, failing with every violation found.
        /// </summary>
        /// <param name="context">
        ///     A context built as the design-time factory builds it: the provider and migrations
        ///     assembly configured, no open connection needed.
        /// </param>
        /// <param name="expectedCollation">The collation the model must declare.</param>
        public static void Verify(DbContext context, string expectedCollation = DatabaseCollations.Platform)
        {
            IReadOnlyList<string> violations = FindViolations(context, expectedCollation);
            if (violations.Count > 0)
            {
                Assert.Fail(
                    $"The collation pin failed for {context.GetType().Name}:{Environment.NewLine}- "
                        + string.Join(Environment.NewLine + "- ", violations));
            }
        }

        /// <summary>Finds every violation of the pin over the context's model and migrations assembly.</summary>
        /// <param name="context">A context built as the design-time factory builds it.</param>
        /// <param name="expectedCollation">The collation the model must declare.</param>
        /// <returns>One sentence per violation; empty when the pin holds.</returns>
        public static IReadOnlyList<string> FindViolations(DbContext context, string expectedCollation = DatabaseCollations.Platform)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedCollation);

            List<string> violations = [];

            // The design-time model is the one migrations are diffed against; the runtime model
            // drops annotations that only design time reads.
            IModel model = context.GetService<IDesignTimeModel>().Model;
            string? collation = model.GetCollation();
            if (!string.Equals(collation, expectedCollation, StringComparison.Ordinal))
            {
                violations.Add($"the model declares the collation '{collation ?? "(none)"}', not '{expectedCollation}'.");
            }

            if (context.Database.HasPendingModelChanges())
            {
                violations.Add("the model has changes that no migration captures; scaffold a migration or revert the change.");
            }

            IMigrationsAssembly migrationsAssembly = context.GetService<IMigrationsAssembly>();
            string provider = context.Database.ProviderName
                ?? throw new InvalidOperationException("The context has no database provider configured.");
            IEnumerable<KeyValuePair<string, Migration>> migrations = migrationsAssembly.Migrations
                .Select(pair => KeyValuePair.Create(pair.Key, migrationsAssembly.CreateMigration(pair.Value, provider)));
            violations.AddRange(FindCollationChanges(migrations));

            return violations;
        }

        /// <summary>Finds every migration operation that changes a database's collation.</summary>
        /// <param name="migrations">The migrations, keyed by id.</param>
        /// <returns>One sentence per offending operation; empty when none changes the collation.</returns>
        public static IReadOnlyList<string> FindCollationChanges(IEnumerable<KeyValuePair<string, Migration>> migrations)
        {
            ArgumentNullException.ThrowIfNull(migrations);

            List<string> violations = [];
            foreach ((string id, Migration migration) in migrations)
            {
                // Both directions: a down that restores an old collation is the same ALTER DATABASE.
                foreach ((string direction, IReadOnlyList<MigrationOperation> operations) in
                    (IEnumerable<(string, IReadOnlyList<MigrationOperation>)>)
                        [("Up", migration.UpOperations), ("Down", migration.DownOperations)])
                {
                    foreach (AlterDatabaseOperation operation in operations.OfType<AlterDatabaseOperation>())
                    {
                        if (!string.Equals(operation.Collation, operation.OldDatabase.Collation, StringComparison.Ordinal))
                        {
                            violations.Add(
                                $"migration '{id}' ({direction}) changes the database collation from "
                                    + $"'{operation.OldDatabase.Collation ?? "(none)"}' to '{operation.Collation ?? "(none)"}'.");
                        }
                    }
                }
            }

            return violations;
        }
    }
}
