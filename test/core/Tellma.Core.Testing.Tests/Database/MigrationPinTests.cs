// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Testing.Database;
using Xunit.Sdk;

namespace Tellma.Core.Testing.Tests.Database
{
    /// <summary>
    ///     The collation and latest-migration pins pass a migrations assembly that keeps the
    ///     platform collation, and name every way one can drift from it: a model in another
    ///     collation, a model ahead of its snapshot, and a migration that alters the collation in
    ///     either direction.
    /// </summary>
    public sealed class MigrationPinTests
    {
        [Fact]
        public void A_model_in_the_platform_collation_with_annotation_only_initial_migration_passes()
        {
            using PinnedContext context = new();

            Assert.Empty(CollationPin.FindViolations(context));
            CollationPin.Verify(context);
        }

        [Fact]
        public void A_model_without_the_platform_collation_and_without_migrations_fails_both_ways()
        {
            using UnpinnedContext context = new();

            IReadOnlyList<string> violations = CollationPin.FindViolations(context);

            Assert.Contains(violations, static v => v.Contains("'(none)'", StringComparison.Ordinal)
                && v.Contains(DatabaseCollations.Platform, StringComparison.Ordinal));
            Assert.Contains(violations, static v => v.Contains("no migration captures", StringComparison.Ordinal));
            Assert.Throws<FailException>(() => CollationPin.Verify(context));
        }

        [Fact]
        public void A_migration_that_changes_the_collation_on_the_way_up_fails()
        {
            IReadOnlyList<string> violations = CollationPin.FindCollationChanges(
                [KeyValuePair.Create<string, Migration>("20260101000000_Recollate", new RecollatingMigration())]);

            Assert.Contains(violations, static v => v.Contains("'20260101000000_Recollate' (Up)", StringComparison.Ordinal)
                && v.Contains("'SQL_Latin1_General_CP1_CI_AS'", StringComparison.Ordinal)
                && v.Contains("'Latin1_General_100_CI_AS'", StringComparison.Ordinal));
        }

        [Fact]
        public void A_migration_that_changes_the_collation_on_the_way_down_fails()
        {
            IReadOnlyList<string> violations = CollationPin.FindCollationChanges(
                [KeyValuePair.Create<string, Migration>("20260101000000_Recollate", new RecollatingMigration())]);

            Assert.Contains(violations, static v => v.Contains("(Down)", StringComparison.Ordinal));
        }

        [Fact]
        public void An_annotation_only_alter_database_passes()
        {
            Assert.Empty(CollationPin.FindCollationChanges(
                [KeyValuePair.Create<string, Migration>("20260101000000_Initial", new PinnedInitial())]));
        }

        [Fact]
        public void The_latest_migration_pin_accepts_the_last_migration()
        {
            using PinnedContext context = new();

            Assert.Equal("20260101000000_Initial", LatestMigrationPin.LastMigrationId(context));
            LatestMigrationPin.Verify(context, "20260101000000_Initial");
        }

        [Fact]
        public void The_latest_migration_pin_refuses_a_stale_constant()
        {
            using PinnedContext context = new();

            FailException failure = Assert.Throws<FailException>(
                () => LatestMigrationPin.Verify(context, "20250101000000_Older"));
            Assert.Contains("20260101000000_Initial", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_latest_migration_pin_refuses_an_assembly_without_migrations()
        {
            using UnpinnedContext context = new();

            Assert.Null(LatestMigrationPin.LastMigrationId(context));
            Assert.Throws<FailException>(() => LatestMigrationPin.Verify(context, "20260101000000_Initial"));
        }

        [Fact]
        public void The_builder_emits_the_operation_the_pin_inspects()
        {
            Assert.IsType<AlterDatabaseOperation>(Assert.Single(new RecollatingMigration().UpOperations));
        }

        /// <summary>A model with no tables in the platform collation, whose migrations are in this assembly.</summary>
        internal sealed class PinnedContext : DbContext
        {
            /// <inheritdoc />
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer(
                    "Server=unused;Database=unused;Encrypt=False",
                    static sql => sql.MigrationsAssembly(typeof(PinnedContext).Assembly.GetName().Name));
            }

            /// <inheritdoc />
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.UseCollation(DatabaseCollations.Platform);
            }
        }

        /// <summary>A model with a table but no collation, and no migrations at all.</summary>
        internal sealed class UnpinnedContext : DbContext
        {
            /// <summary>The one table, which no migration creates.</summary>
            public DbSet<UnpinnedRow> Rows => Set<UnpinnedRow>();

            /// <inheritdoc />
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer(
                    "Server=unused;Database=unused;Encrypt=False",
                    static sql => sql.MigrationsAssembly(typeof(UnpinnedContext).Assembly.GetName().Name));
            }
        }

        /// <summary>The unpinned context's row.</summary>
        internal sealed class UnpinnedRow
        {
            /// <summary>The key.</summary>
            public int Id { get; set; }
        }

        /// <summary>The pinned context's initial migration, carrying an annotation-only database operation.</summary>
        [DbContext(typeof(PinnedContext))]
        [Migration("20260101000000_Initial")]
        internal sealed class PinnedInitial : Migration
        {
            /// <inheritdoc />
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterDatabase().Annotation("Example:Annotation", "value");
            }

            /// <inheritdoc />
            protected override void Down(MigrationBuilder migrationBuilder)
            {
            }
        }

        /// <summary>The pinned context's snapshot, matching its model exactly.</summary>
        [DbContext(typeof(PinnedContext))]
        internal sealed class PinnedContextModelSnapshot : ModelSnapshot
        {
            /// <inheritdoc />
            protected override void BuildModel(ModelBuilder modelBuilder)
            {
                modelBuilder
                    .UseCollation(DatabaseCollations.Platform)
                    .HasAnnotation("ProductVersion", "10.0.11")
                    .HasAnnotation("Relational:MaxIdentifierLength", 128);

                SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);
            }
        }

        /// <summary>A migration that moves the collation forward and back: forbidden both ways.</summary>
        private sealed class RecollatingMigration : Migration
        {
            /// <inheritdoc />
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterDatabase(collation: DatabaseCollations.Platform, oldCollation: "SQL_Latin1_General_CP1_CI_AS");
            }

            /// <inheritdoc />
            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterDatabase(collation: "SQL_Latin1_General_CP1_CI_AS", oldCollation: DatabaseCollations.Platform);
            }
        }
    }
}
