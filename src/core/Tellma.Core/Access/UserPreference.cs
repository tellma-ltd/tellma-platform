// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     A row of <c>core.UserPreferences</c>: one key of a user's opaque preference bag. The server
    ///     enforces the key grammar and the caps and interprets nothing.
    /// </summary>
    /// <remarks>
    ///     Not an entity: no surrogate key, no sequence, no history, no audit columns; absent from the
    ///     query language and from Excel. It is written through <see cref="UserPreferenceList" /> and
    ///     deleted with its user.
    /// </remarks>
    internal sealed class UserPreference
    {
        /// <summary>The user.</summary>
        public int UserId { get; set; }

        /// <summary>The key, dotted ASCII segments such as <c>nav.pinned</c>, <c>varchar(128)</c>.</summary>
        public string Key { get; set; } = null!;

        /// <summary>The value, opaque to the server and JSON by convention, <c>nvarchar(max)</c>.</summary>
        public string Value { get; set; } = null!;
    }

    /// <summary>The model of <c>core.UserPreferences</c>, keyed by user and key and deleted with the user.</summary>
    internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<UserPreference> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table: keyed by user and key, not system-versioned ---
            _ = builder.ToTable("UserPreferences", "core");
            _ = builder.HasKey(static preference => new { preference.UserId, preference.Key });
            _ = builder.Property(static preference => preference.UserId).ValueGeneratedNever();
            _ = builder.Property(static preference => preference.Key).HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false);

            // --- Deleted with the user, whichever leaf the distribution registers ---
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The user preferences");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(UserPreference.UserId))
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
