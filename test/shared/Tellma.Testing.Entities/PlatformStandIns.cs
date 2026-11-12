// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Testing.Entities
{
    // Stand-ins for the core tables the fixture schema references that no feature contributes yet,
    // under their production names, so the job foreign keys and the version-tag statements resolve
    // to the tables production has. Each is replaced, and the fixture migrations regenerated, when
    // the feature that owns the production table contributes it: core.VersionTags with the caching
    // tables, core.Jobs with the job runtime. Core itself contributes core.Users, the other access
    // tables and core.Blobs to every composition.

    /// <summary>A stand-in for <c>core.Jobs</c>, which every job entity's <c>JobId</c> references.</summary>
    [Table("Jobs", Schema = PlatformSchema.Name)]
    public class FixtureJob : Entity<int>;

    /// <summary>A row of the stand-in <c>core.VersionTags</c>: one named tag.</summary>
    public sealed class FixtureVersionTag
    {
        /// <summary>The tag's name, <c>nvarchar(128)</c>; the key.</summary>
        public string Name { get; set; } = null!;

        /// <summary>The tag's current value.</summary>
        public Guid Tag { get; set; }
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

    /// <summary>The platform schema's name.</summary>
    public static class PlatformSchema
    {
        /// <summary>The schema of the platform's core tables.</summary>
        public const string Name = "core";
    }
}
