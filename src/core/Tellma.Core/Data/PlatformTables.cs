// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     A row of <c>dbo.__TellmaSchema</c>: one storage fingerprint of the tenant model the database
    ///     was migrated to. The migrator inserts its model's fingerprint after a migration and keeps the
    ///     two newest rows, and every batch's first statement refuses a database that does not list the
    ///     running model's fingerprint.
    /// </summary>
    /// <remarks>
    ///     Mapped for migrations only: the platform's entity metadata and the query schema skip it, and
    ///     of <c>dbo</c>'s tables the application role reads this one alone.
    /// </remarks>
    internal sealed class TellmaSchemaRow
    {
        /// <summary>The lower-case hex SHA-256 storage fingerprint; the key, <c>char(64)</c>.</summary>
        public string Fingerprint { get; set; } = null!;

        /// <summary>The last migration applied when the row was written, <c>nvarchar(150)</c>.</summary>
        public string MigrationId { get; set; } = null!;

        /// <summary>The platform version of the migrator that wrote the row, <c>nvarchar(64)</c>.</summary>
        public string PlatformVersion { get; set; } = null!;

        /// <summary>When the row was written, <c>datetimeoffset(3)</c>.</summary>
        public DateTimeOffset AppliedAt { get; set; }
    }

    /// <summary>
    ///     A row of <c>dbo.__TellmaProvisioning</c>: one provisioning step's completion in a tenant
    ///     database. A step runs again when its version exceeds the recorded one.
    /// </summary>
    /// <remarks>Mapped for migrations only, like <see cref="TellmaSchemaRow" />.</remarks>
    internal sealed class TellmaProvisioningRow
    {
        /// <summary>The step's name, such as <c>core.settings</c>; the key, <c>nvarchar(128)</c>.</summary>
        public string Step { get; set; } = null!;

        /// <summary>The version of the step that completed.</summary>
        public int Version { get; set; }

        /// <summary>When the step completed, <c>datetimeoffset(3)</c>.</summary>
        public DateTimeOffset CompletedAt { get; set; }

        /// <summary>The platform version of the migrator that ran the step, <c>nvarchar(64)</c>.</summary>
        public string PlatformVersion { get; set; } = null!;
    }

    /// <summary>The model of <c>dbo.__TellmaSchema</c>, which Core contributes to every tenant model.</summary>
    internal sealed class TellmaSchemaRowConfiguration : IEntityTypeConfiguration<TellmaSchemaRow>
    {
        /// <summary>The table.</summary>
        public const string Table = "__TellmaSchema";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TellmaSchemaRow> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable(Table, PlatformTables.Schema);
            _ = builder.HasKey(static row => row.Fingerprint);
            _ = builder.Property(static row => row.Fingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            _ = builder.Property(static row => row.MigrationId).HasMaxLength(150);
            _ = builder.Property(static row => row.PlatformVersion).HasMaxLength(64);
            _ = builder.Property(static row => row.AppliedAt).HasPrecision(3);
        }
    }

    /// <summary>The model of <c>dbo.__TellmaProvisioning</c>, which Core contributes to every tenant model.</summary>
    internal sealed class TellmaProvisioningRowConfiguration : IEntityTypeConfiguration<TellmaProvisioningRow>
    {
        /// <summary>The table.</summary>
        public const string Table = "__TellmaProvisioning";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TellmaProvisioningRow> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable(Table, PlatformTables.Schema);
            _ = builder.HasKey(static row => row.Step);
            _ = builder.Property(static row => row.Step).HasMaxLength(128);
            _ = builder.Property(static row => row.Version).ValueGeneratedNever();
            _ = builder.Property(static row => row.CompletedAt).HasPrecision(3);
            _ = builder.Property(static row => row.PlatformVersion).HasMaxLength(64);
        }
    }

    /// <summary>The platform's own tables in a tenant database, which the model maps for migrations only.</summary>
    internal static class PlatformTables
    {
        /// <summary>The schema of the platform's own tables.</summary>
        public const string Schema = "dbo";

        /// <summary>The row classes of the platform's own tables.</summary>
        public static IReadOnlyList<Type> RowTypes { get; } = [typeof(TellmaSchemaRow), typeof(TellmaProvisioningRow)];
    }
}
