// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Data;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     A row of <c>core.UserStamps</c>: the columns of a user that change often (the activity stamp,
    ///     the two user tags, the inbox and assignments tracking), kept beside the system-versioned user
    ///     row so that writing them never writes a history row.
    /// </summary>
    /// <remarks>
    ///     Not an entity: no surrogate key, no sequence, no table type, no audit columns. Every user has
    ///     exactly one, inserted in the batch that inserts the user, and it is deleted with the user. The
    ///     query language reads it as <c>core.UserStamp</c>, read-only.
    /// </remarks>
    internal sealed class UserStamp
    {
        /// <summary>The user, the row's key.</summary>
        public int UserId { get; set; }

        /// <summary>When the user was last active, <c>datetimeoffset(3)</c>; written at most once per throttle interval.</summary>
        public DateTimeOffset? LastActiveAt { get; set; }

        /// <summary>The user's permissions tag, moved by every write of the user's role memberships.</summary>
        public Guid PermissionsTag { get; set; }

        /// <summary>The user's preferences tag, moved by every write of the user's own row and preferences.</summary>
        public Guid PreferencesTag { get; set; }

        /// <summary>When the user last marked the inbox seen, <c>datetimeoffset(3)</c>.</summary>
        public DateTimeOffset? InboxSeenAt { get; set; }

        /// <summary>The user's inbox version, incremented by every change to the user's inbox.</summary>
        public long InboxVersion { get; set; }

        /// <summary>When the user last marked the assignments seen, <c>datetimeoffset(3)</c>.</summary>
        public DateTimeOffset? AssignmentsSeenAt { get; set; }
    }

    /// <summary>The model of <c>core.UserStamps</c>, one row per user, deleted with the user, and the system user's row.</summary>
    internal sealed class UserStampConfiguration : IEntityTypeConfiguration<UserStamp>
    {
        /// <summary>The query language's name for the table, which it reads but never writes.</summary>
        public const string QueryexName = "core.UserStamp";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<UserStamp> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table: keyed by its user, not system-versioned ---
            _ = builder.ToTable("UserStamps", "core");
            _ = builder.HasKey(static stamp => stamp.UserId);
            _ = builder.Property(static stamp => stamp.UserId).ValueGeneratedNever();
            _ = builder.HasAnnotation(TellmaAnnotationNames.QueryexReadOnlyEntity, QueryexName);

            // --- Defaults: fresh tags and an empty inbox for every new row ---
            _ = builder.Property(static stamp => stamp.PermissionsTag).HasDefaultValueSql("NEWID()");
            _ = builder.Property(static stamp => stamp.PreferencesTag).HasDefaultValueSql("NEWID()");
            _ = builder.Property(static stamp => stamp.InboxVersion).HasDefaultValue(0L);

            // --- One row per user, whichever leaf the distribution registers, deleted with it ---
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The user stamps");
            _ = builder.HasOne(user.ClrType)
                .WithOne()
                .HasForeignKey(typeof(UserStamp), nameof(UserStamp.UserId))
                .OnDelete(DeleteBehavior.Cascade);

            // --- The system user's row, with fixed tags ---
            _ = builder.HasData(new UserStamp
            {
                UserId = WellKnownIds.SystemUserId,
                PermissionsTag = AccessSeeds.SystemPermissionsTag,
                PreferencesTag = AccessSeeds.SystemPreferencesTag,
                InboxVersion = 0,
            });
        }
    }
}
