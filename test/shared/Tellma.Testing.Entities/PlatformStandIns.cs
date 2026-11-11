// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Testing.Entities
{
    // Stand-ins for the core tables the fixture schema references, under their production names, so
    // the audit, job and blob foreign keys, the version-tag statements and the user stamps resolve
    // to the tables production has. Each is replaced, and the fixture migrations regenerated, when
    // the feature that owns the production table contributes it: core.Users and core.UserStamps
    // with the access tables, core.VersionTags with the caching tables, core.Blobs with the blob
    // runtime, core.Jobs with the job runtime.

    /// <summary>A stand-in for <c>core.Users</c>, which every audit column references.</summary>
    [Table("Users", Schema = PlatformSchema.Name)]
    public class FixtureUser : Entity<int>, IActivatable
    {
        /// <summary>The user's name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <inheritdoc />
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A stand-in for <c>core.Jobs</c>, which every job entity's <c>JobId</c> references.</summary>
    [Table("Jobs", Schema = PlatformSchema.Name)]
    public class FixtureJob : Entity<int>;

    /// <summary>
    ///     A stand-in for <c>core.Blobs</c> and its sequence <c>core.sq_Blobs</c>, which every blob
    ///     reference resolves to.
    /// </summary>
    [Table("Blobs", Schema = PlatformSchema.Name)]
    public class FixtureBlob : Entity<int>
    {
        /// <summary>The blob's kind.</summary>
        [Column(TypeName = "varchar(64)")]
        public string Kind { get; set; } = null!;
    }

    /// <summary>A row of the stand-in <c>core.VersionTags</c>: one named tag.</summary>
    public sealed class FixtureVersionTag
    {
        /// <summary>The tag's name, <c>nvarchar(128)</c>; the key.</summary>
        public string Name { get; set; } = null!;

        /// <summary>The tag's current value.</summary>
        public Guid Tag { get; set; }
    }

    /// <summary>A row of the stand-in <c>core.UserStamps</c>: one user's per-user tags.</summary>
    public sealed class FixtureUserStamp
    {
        /// <summary>The user; the key.</summary>
        public int UserId { get; set; }

        /// <summary>When the user was last active.</summary>
        public DateTimeOffset? LastActiveAt { get; set; }

        /// <summary>The user's permissions tag.</summary>
        public Guid PermissionsTag { get; set; }

        /// <summary>The user's preferences tag.</summary>
        public Guid PreferencesTag { get; set; }
    }

    /// <summary>The model of the stand-in <c>core.VersionTags</c>.</summary>
    public sealed class FixtureVersionTagConfiguration : IEntityTypeConfiguration<FixtureVersionTag>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<FixtureVersionTag> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("VersionTags", PlatformSchema.Name);
            _ = builder.HasKey(static row => row.Name).HasName("PK_VersionTags");
            _ = builder.Property(static row => row.Name).HasMaxLength(128);
            _ = builder.Property(static row => row.Tag).HasDefaultValueSql("NEWID()");
        }
    }

    /// <summary>The model of the stand-in <c>core.UserStamps</c>, deleted with its user.</summary>
    public sealed class FixtureUserStampConfiguration : IEntityTypeConfiguration<FixtureUserStamp>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<FixtureUserStamp> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("UserStamps", PlatformSchema.Name);
            _ = builder.HasKey(static row => row.UserId).HasName("PK_UserStamps");
            _ = builder.Property(static row => row.UserId).ValueGeneratedNever();
            _ = builder.Property(static row => row.LastActiveAt).HasPrecision(3);
            _ = builder.Property(static row => row.PermissionsTag).HasDefaultValueSql("NEWID()");
            _ = builder.Property(static row => row.PreferencesTag).HasDefaultValueSql("NEWID()");
            _ = builder.HasOne<FixtureUser>()
                .WithOne()
                .HasForeignKey<FixtureUserStamp>(static row => row.UserId)
                .HasConstraintName("FK_UserStamps_UserId")
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>The platform schema's name.</summary>
    public static class PlatformSchema
    {
        /// <summary>The schema of the platform's core tables.</summary>
        public const string Name = "core";
    }
}
