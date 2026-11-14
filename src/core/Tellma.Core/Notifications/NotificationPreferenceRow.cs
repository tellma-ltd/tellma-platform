// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     A row of <c>core.NotificationPreferences</c>: one user's deviation from a channel's default
    ///     for one notification type. A missing row means the channel's default.
    /// </summary>
    /// <remarks>
    ///     Not an entity: no surrogate key, no sequence, no history, no audit columns; absent from the
    ///     query language and from Excel, with no table type of its own. The preferences service
    ///     writes it through <see cref="Abstractions.TableTypes.NotificationPreferenceList" />, and it is
    ///     deleted with its user.
    /// </remarks>
    internal sealed class NotificationPreferenceRow
    {
        /// <summary>The longest channel key, <c>varchar(16)</c>.</summary>
        public const int ChannelLength = 16;

        /// <summary>The user.</summary>
        public int UserId { get; set; }

        /// <summary>A registered type key, <c>nvarchar(64)</c>.</summary>
        public string Type { get; set; } = null!;

        /// <summary>A registered channel key, <c>varchar(16)</c>.</summary>
        public string Channel { get; set; } = null!;

        /// <summary>Whether the user receives the type through the channel.</summary>
        public bool Enabled { get; set; }
    }

    /// <summary>The model of <c>core.NotificationPreferences</c>, keyed by user, type and channel and deleted with the user.</summary>
    internal sealed class NotificationPreferenceRowConfiguration : IEntityTypeConfiguration<NotificationPreferenceRow>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<NotificationPreferenceRow> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table: keyed by user, type and channel, not system-versioned ---
            _ = builder.ToTable("NotificationPreferences", "core");
            _ = builder.HasKey(static preference => new { preference.UserId, preference.Type, preference.Channel });
            _ = builder.Property(static preference => preference.UserId).ValueGeneratedNever();
            _ = builder.Property(static preference => preference.Type).HasMaxLength(Notification.TypeLength);
            _ = builder.Property(static preference => preference.Channel)
                .HasColumnType("varchar(16)")
                .HasMaxLength(NotificationPreferenceRow.ChannelLength)
                .IsUnicode(false);

            // --- Deleted with the user, whichever leaf the distribution registers ---
            // Only a New user is ever deleted, and one cannot have saved preferences: the cascade
            // keeps the schema honest rather than serving a path that runs.
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The notification preferences");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(NotificationPreferenceRow.UserId))
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
