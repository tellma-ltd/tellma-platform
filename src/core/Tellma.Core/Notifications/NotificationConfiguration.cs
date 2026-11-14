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
using Tellma.Core.EntityFrameworkCore.LockEscalation;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The model configuration of <c>core.Notifications</c>: the column widths the class does not
    ///     carry, the recipient and the actor as users, the four indexes the notifier, the inbox and
    ///     retention seek, and no lock escalation.
    /// </summary>
    /// <remarks>
    ///     The conventions already give the table its sequence <c>core.sq_Notifications</c>, its
    ///     positive-key check, its <c>datetimeoffset(3)</c> timestamps, the severity as non-Unicode
    ///     text, and its table type over every column.
    /// </remarks>
    internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Notification> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Column widths ---
            _ = builder.Property(static notification => notification.Type).HasMaxLength(Notification.TypeLength);
            _ = builder.Property(static notification => notification.Severity).HasMaxLength(Notification.SeverityLength);
            _ = builder.Property(static notification => notification.ArgumentsJson).HasMaxLength(Notification.ArgumentsLength);
            _ = builder.Property(static notification => notification.TargetResource).HasMaxLength(Notification.TargetResourceLength).IsUnicode(false);
            _ = builder.Property(static notification => notification.DedupKey).HasMaxLength(Notification.DedupKeyLength);

            // --- The recipient and the actor, whichever leaf the distribution registers ---
            // No cascade: a user is never deleted once past New, and deleting a New user with rows
            // is the pipeline's foreign-key refusal, which is the correct answer.
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The recipient of core.Notifications");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Notification.UserId))
                .OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Notification.ActorUserId))
                .OnDelete(DeleteBehavior.NoAction);

            // --- The indexes, each the seek of the statements named beside it ---
            // The inbox summary's unseen count and severity, the latest page and the clear-read verb.
            _ = builder.HasIndex(static notification => new { notification.UserId, notification.CreatedAt, notification.Id })
                .HasDatabaseName("IX_Notifications_User")
                .IsDescending(false, true, true)
                .IncludeProperties(static notification => new { notification.ReadAt, notification.Type, notification.Severity });

            // The unread count and the read verbs.
            _ = builder.HasIndex(static notification => notification.UserId)
                .HasDatabaseName("IX_Notifications_Unread")
                .HasFilter("[ReadAt] IS NULL");

            // The duplicate predicate, the replace and the withdrawal; ReadAt a residual. Not unique:
            // a duplicate is best effort, and a race must never fail the save that raised it.
            _ = builder.HasIndex(static notification => new { notification.DedupKey, notification.UserId })
                .HasDatabaseName("IX_Notifications_Dedup")
                .HasFilter("[DedupKey] IS NOT NULL")
                .IncludeProperties(static notification => notification.ReadAt);

            // The retention pages, by band and age.
            _ = builder.HasIndex(static notification => new { notification.ReadAt, notification.CreatedAt })
                .HasDatabaseName("IX_Notifications_Retention");

            // --- The read verbs touch one user's rows in one statement and never escalate to a table lock ---
            _ = builder.HasLockEscalation(LockEscalationMode.Disable);
        }
    }
}
