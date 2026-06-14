// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.EntityFrameworkCore.MigrationsHost.Model;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.MigrationsHost
{
    /// <summary>
    ///     The migrator-shaped host's context: a representative model exercising the table-types
    ///     feature surface — fluent and attribute opt-ins, pack→leaf attribute inheritance, column
    ///     exclusion, rowversion, computed-column exclusion, grants, standalone types (the
    ///     platform's bulk shapes from Tellma.Core.Abstractions plus a custom one), per-table ID
    ///     sequences, and reserved-band seed data.
    /// </summary>
    /// <param name="options">The context options.</param>
    public class MigrationsHostContext(DbContextOptions<MigrationsHostContext> options) : DbContext(options)
    {
        /// <summary>
        ///     The reserved seed band: <c>HasData</c> keys must stay at or below this value, and
        ///     every per-table ID sequence starts above it (see ARCHITECTURE.md → ID allocation).
        /// </summary>
        public const int SeedBandMax = 9_999;

        /// <summary>The customers (pack→leaf inheritance sample).</summary>
        public DbSet<Customer> Customers => Set<Customer>();

        /// <summary>The invoices (fluent opt-in sample).</summary>
        public DbSet<Invoice> Invoices => Set<Invoice>();

        /// <summary>The invoice lines (attribute opt-in sample).</summary>
        public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

        /// <summary>The settings (no table type).</summary>
        public DbSet<AppSetting> Settings => Set<AppSetting>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // The platform's canonical bulk shapes (plain classes in Tellma.Core.Abstractions),
            // registered through the same standalone route as any custom shape — the composition
            // does this once per distribution.
            modelBuilder.HasTableType<IdList>(schema: "dbo", buildAction: type => type.HasGrants("public"));
            modelBuilder.HasTableType<BigIdList>(schema: "dbo", buildAction: type => type.HasGrants("public"));
            modelBuilder.HasTableType<GuidList>(schema: "dbo", buildAction: type => type.HasGrants("public"));
            modelBuilder.HasTableType<StringList>(schema: "dbo", buildAction: type => type.HasGrants("public"));

            // A custom standalone table type (spec 0001 §5) derived from a plain class: bulk state
            // updates without a paired table.
            modelBuilder.HasTableType<DocumentState>(buildAction: type => type.HasGrants("public"));

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("Customers", "crm");
                // No IDENTITY anywhere: IDs are app-assigned from the per-table sequences below
                // (the allocator that draws from them is specified separately).
                entity.Property(c => c.Id).ValueGeneratedNever();
                // Opt-in comes from the inherited [TableType] on CustomerBase; only grants are
                // added fluently here.
                entity.HasTableTypeGrants("public");
            });

            // Per-table ID sequences per the platform convention (spec 0001 §4: plain EF sequences;
            // the table-types library ships no sequence support). Sequences start above the
            // reserved seed band so well-known seeded IDs stay deterministic.
            modelBuilder.HasSequence<int>("sq_Customers", "crm").StartsAt(SeedBandMax + 1);
            modelBuilder.HasSequence<int>("sq_Invoices", "gl").StartsAt(SeedBandMax + 1);
            modelBuilder.HasSequence<int>("sq_InvoiceLines", "gl").StartsAt(SeedBandMax + 1);
            modelBuilder.HasSequence<int>("sq_Settings", "dbo").StartsAt(SeedBandMax + 1);

            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.ToTable("Invoices", "gl");
                entity.Property(i => i.Id).ValueGeneratedNever();
                entity.HasTableType();
                entity.HasTableTypeGrants("public");
                entity.Property(i => i.Total).HasColumnType("decimal(19,4)");
                entity.Property(i => i.TotalWithTax).HasColumnType("decimal(19,4)")
                    .HasComputedColumnSql("[Total] * 1.15", stored: true);
                entity.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            });

            modelBuilder.Entity<InvoiceLine>(entity =>
            {
                entity.ToTable("InvoiceLines", "gl");
                entity.Property(l => l.Id).ValueGeneratedNever();
                // Opt-in comes from [TableType(Name = "InvoiceLinesList")]; add grants fluently.
                entity.HasTableTypeGrants("public");
            });

            modelBuilder.Entity<AppSetting>(entity =>
            {
                entity.ToTable("Settings", "dbo");
                entity.Property(s => s.Id).ValueGeneratedNever();
                // Well-known rows whose IDs code references: confined to the reserved band.
                entity.HasData(
                    new AppSetting { Id = 1, Key = "System.Version", Value = "1.0" },
                    new AppSetting { Id = 2, Key = "System.Locale", Value = "en" });
            });
        }
    }
}
