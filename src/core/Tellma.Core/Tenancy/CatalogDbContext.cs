// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Tenancy.Catalog;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The EF context of a distribution's catalog database: schema <c>catalog</c> with its four
    ///     tables, the tenant id sequence and the membership table type. It exists to carry the
    ///     platform-owned catalog migrations and the table type's definition; the catalog's
    ///     statements read and write the tables directly, and no host queries the catalog through
    ///     it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The catalog keeps its own conventions rather than the tenant model's: explicit
    ///         constraint and index names throughout, <c>datetimeoffset(3)</c> timestamps, enum columns
    ///         as <c>nvarchar(16)</c> with <c>CHECK</c> constraints, no identity columns and no
    ///         temporal tables. The membership table type is registered as the tenant model registers
    ///         its standalone types, so its subject column takes the binary collation its attribute
    ///         declares, and it grants <c>EXECUTE</c> to the application role.
    ///     </para>
    ///     <para>
    ///         The model is set to the platform collation, so a database EF creates for a development
    ///         or test run is created in it. Its migrations live in the <c>Tellma.Core.Migrator</c>
    ///         assembly, with their history in <c>catalog.__EFMigrationsHistory</c>.
    ///     </para>
    /// </remarks>
    /// <param name="options">Options configured with <see cref="Configure" />.</param>
    public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
    {
        /// <summary>The catalog's schema.</summary>
        public const string Schema = "catalog";

        /// <summary>The name of the catalog's migration history table, in <see cref="Schema" />.</summary>
        public const string HistoryTable = "__EFMigrationsHistory";

        /// <summary>The assembly that holds the catalog's migrations.</summary>
        public const string MigrationsAssembly = "Tellma.Core.Migrator";

        /// <summary>The tenant id sequence, which starts above the band of requested ids.</summary>
        public const string TenantSequence = "sq_Tenants";

        /// <summary>The first id the tenant sequence issues; ids below it are only ever requested.</summary>
        public const int FirstSequenceId = 1000;

        /// <summary>
        ///     The version the first migration seeds <c>catalog.CatalogState</c> with. Every write
        ///     replaces it, and a registry compares versions for inequality only, so a fixed seed is as
        ///     good as a random one.
        /// </summary>
        public static readonly Guid InitialVersion = new("00000000-0000-0000-0000-000000000001");

        /// <summary>The time the first migration seeds <c>catalog.CatalogState</c> with.</summary>
        public static readonly DateTimeOffset InitialModifiedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        ///     Configures a builder for the catalog: SQL Server with the catalog's migrations assembly
        ///     and history table, the platform's pinned compatibility level, and the table-type
        ///     extension under the catalog's sweep scope.
        /// </summary>
        /// <param name="builder">The options builder.</param>
        /// <param name="connectionString">The catalog's connection string, or null for a context that only builds the model.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder" /> is null.</exception>
        public static DbContextOptionsBuilder<CatalogDbContext> Configure(DbContextOptionsBuilder<CatalogDbContext> builder, string? connectionString)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = connectionString is null
                ? builder.UseSqlServer(ConfigureSqlServer)
                : builder.UseSqlServer(connectionString, ConfigureSqlServer);
            _ = builder.UseTableTypes(TableTypeSweepScopes.Catalog);
            return builder;

            static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
            {
                _ = sql.MigrationsAssembly(MigrationsAssembly);
                _ = sql.MigrationsHistoryTable(HistoryTable, Schema);
                _ = sql.UseCompatibilityLevel(TellmaSqlServerDbContextOptionsExtensions.CompatibilityLevel);
            }
        }

        /// <summary>Creates a catalog context.</summary>
        /// <param name="connectionString">The catalog's connection string, or null for a context that only builds the model.</param>
        /// <returns>The context; the caller disposes it.</returns>
        public static CatalogDbContext Create(string? connectionString)
        {
            return new CatalogDbContext(Configure(new DbContextOptionsBuilder<CatalogDbContext>(), connectionString).Options);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            // --- Model-wide: the platform collation, the schema and the tenant id sequence ---
            _ = modelBuilder.UseCollation(DatabaseCollations.Platform);
            _ = modelBuilder.HasDefaultSchema(Schema);
            _ = modelBuilder.HasSequence<int>(TenantSequence, Schema).StartsAt(FirstSequenceId).IncrementsBy(1);

            ConfigureTenants(modelBuilder);
            ConfigureCatalogState(modelBuilder);
            ConfigureMemberships(modelBuilder);
            ConfigureSessions(modelBuilder);

            // --- The membership table type, registered as the tenant model registers its standalone types ---
            TableTypeConvention.AddStandalone(modelBuilder, typeof(TenantMembershipRecord));
        }

        /// <summary>Maps <c>catalog.Tenants</c>.</summary>
        /// <param name="modelBuilder">The model builder.</param>
        private static void ConfigureTenants(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<CatalogTenant>(tenant =>
            {
                _ = tenant.ToTable("Tenants", Schema, table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_Tenants_Names",
                        "((([Name2] IS NULL AND [Language2] IS NULL) OR ([Name2] IS NOT NULL AND [Language2] IS NOT NULL)) AND (([Name3] IS NULL AND [Language3] IS NULL) OR ([Name3] IS NOT NULL AND [Language3] IS NOT NULL)))");
                    _ = table.HasCheckConstraint("CK_Tenants_Category", "[Category] IN (N'Live', N'Sandbox')");
                    _ = table.HasCheckConstraint("CK_Tenants_LiveTenantId", "[Category] = N'Sandbox' OR [LiveTenantId] IS NULL");
                    _ = table.HasCheckConstraint(
                        "CK_Tenants_State",
                        "[State] IN (N'Provisioning', N'Active', N'ReadOnly', N'Frozen', N'Suspended', N'Retired')");
                    _ = table.HasCheckConstraint("CK_Tenants_Server", "[Server] NOT LIKE N'%[;=]%'");
                    _ = table.HasCheckConstraint("CK_Tenants_Database", "[Database] NOT LIKE N'%[;=]%'");
                });

                // --- Key: allocated by the registration statement, never by the database ---
                _ = tenant.HasKey(static row => row.Id).HasName("PK_Tenants");
                _ = tenant.Property(static row => row.Id).ValueGeneratedNever();

                // --- Columns ---
                _ = tenant.Property(static row => row.Name).HasMaxLength(255);
                _ = tenant.Property(static row => row.Name2).HasMaxLength(255);
                _ = tenant.Property(static row => row.Name3).HasMaxLength(255);
                _ = tenant.Property(static row => row.Language1).HasMaxLength(35).IsUnicode(false);
                _ = tenant.Property(static row => row.Language2).HasMaxLength(35).IsUnicode(false);
                _ = tenant.Property(static row => row.Language3).HasMaxLength(35).IsUnicode(false);
                _ = tenant.Property(static row => row.Category).HasMaxLength(16);
                _ = tenant.Property(static row => row.State).HasMaxLength(16);
                _ = tenant.Property(static row => row.StateReason).HasMaxLength(1024);
                _ = tenant.Property(static row => row.StateChangedAt).HasPrecision(3);
                _ = tenant.Property(static row => row.StateChangedBy).HasMaxLength(128);
                _ = tenant.Property(static row => row.Server).HasMaxLength(255);
                _ = tenant.Property(static row => row.Database).HasMaxLength(128);
                _ = tenant.Property(static row => row.CredentialProfile)
                    .HasMaxLength(64)
                    .HasDefaultValue(TellmaSqlOptions.DefaultProfile, "DF_Tenants_CredentialProfile");
                _ = tenant.Property(static row => row.CreatedAt).HasPrecision(3);
                _ = tenant.Property(static row => row.ModifiedAt).HasPrecision(3);

                // --- A sandbox's source live tenant ---
                _ = tenant.HasOne<CatalogTenant>()
                    .WithMany()
                    .HasForeignKey(static row => row.LiveTenantId)
                    .HasConstraintName("FK_Tenants_LiveTenantId")
                    .OnDelete(DeleteBehavior.NoAction);

                // --- Indexes ---
                _ = tenant.HasIndex(static row => row.State).HasDatabaseName("IX_Tenants_State");
                _ = tenant.HasIndex(static row => row.LiveTenantId)
                    .HasDatabaseName("IX_Tenants_LiveTenantId")
                    .HasFilter("[LiveTenantId] IS NOT NULL");
                _ = tenant.HasIndex(static row => new { row.Server, row.Database })
                    .HasDatabaseName("UX_Tenants_Location")
                    .IsUnique();
            });
        }

        /// <summary>Maps <c>catalog.CatalogState</c> and seeds its one row.</summary>
        /// <param name="modelBuilder">The model builder.</param>
        private static void ConfigureCatalogState(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<CatalogStateRow>(state =>
            {
                _ = state.ToTable("CatalogState", Schema, static table => table.HasCheckConstraint("CK_CatalogState_Id", "[Id] = 1"));
                _ = state.HasKey(static row => row.Id).HasName("PK_CatalogState");
                _ = state.Property(static row => row.Id).ValueGeneratedNever();
                _ = state.Property(static row => row.ModifiedAt).HasPrecision(3);

                // The one row exists from the first migration, so the refresh probe always finds it.
                _ = state.HasData(new CatalogStateRow { Id = 1, Version = InitialVersion, ModifiedAt = InitialModifiedAt });
            });
        }

        /// <summary>Maps <c>catalog.TenantMemberships</c>.</summary>
        /// <param name="modelBuilder">The model builder.</param>
        private static void ConfigureMemberships(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<CatalogTenantMembership>(membership =>
            {
                _ = membership.ToTable("TenantMemberships", Schema);
                _ = membership.HasKey(static row => new { row.Subject, row.TenantId }).HasName("PK_TenantMemberships");

                // Case-sensitive, as the tenant database compares subjects; the membership table type's
                // subject column declares the same collation, so the two compare without a conflict.
                _ = membership.Property(static row => row.Subject)
                    .HasMaxLength(255)
                    .IsUnicode(false)
                    .UseCollation(DatabaseCollations.Binary);
                _ = membership.Property(static row => row.UpdatedAt).HasPrecision(3);

                _ = membership.HasOne<CatalogTenant>()
                    .WithMany()
                    .HasForeignKey(static row => row.TenantId)
                    .HasConstraintName("FK_TenantMemberships_TenantId")
                    .OnDelete(DeleteBehavior.NoAction);

                _ = membership.HasIndex(static row => row.TenantId).HasDatabaseName("IX_TenantMemberships_TenantId");
            });
        }

        /// <summary>Maps <c>catalog.Sessions</c>.</summary>
        /// <param name="modelBuilder">The model builder.</param>
        private static void ConfigureSessions(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<CatalogSession>(session =>
            {
                _ = session.ToTable("Sessions", Schema);
                _ = session.HasKey(static row => row.Key).HasName("PK_Sessions");

                // Keys, handles and subjects are identifiers, compared byte for byte.
                _ = session.Property(static row => row.Key).HasMaxLength(64).IsUnicode(false).UseCollation(DatabaseCollations.Binary);
                _ = session.Property(static row => row.Handle)
                    .HasMaxLength(22)
                    .IsFixedLength()
                    .IsUnicode(false)
                    .UseCollation(DatabaseCollations.Binary);
                _ = session.Property(static row => row.Subject).HasMaxLength(255).IsUnicode(false).UseCollation(DatabaseCollations.Binary);
                _ = session.Property(static row => row.Sid).HasMaxLength(128);
                _ = session.Property(static row => row.CreatedAt).HasPrecision(3);
                _ = session.Property(static row => row.RenewedAt).HasPrecision(3);
                _ = session.Property(static row => row.ExpiresAt).HasPrecision(3);

                _ = session.HasIndex(static row => row.Sid).HasDatabaseName("IX_Sessions_Sid").HasFilter("[Sid] IS NOT NULL");
                _ = session.HasIndex(static row => row.Subject).HasDatabaseName("IX_Sessions_Subject");
                _ = session.HasIndex(static row => row.ExpiresAt).HasDatabaseName("IX_Sessions_ExpiresAt");
            });
        }
    }
}
