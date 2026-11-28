// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     The model configuration of <c>core.OutgoingEmails</c>: the two server-owned columns' database
    ///     defaults, the addressed member as a user, and the three indexes an administrator's lists and
    ///     the per-record history seek.
    /// </summary>
    /// <remarks>
    ///     The class's attributes carry the column widths. The conventions give the table its sequence
    ///     <c>core.sq_OutgoingEmails</c>, its positive-key check, its <c>datetimeoffset(3)</c>
    ///     timestamps, the state, audience and delivery status as non-Unicode text, the job's foreign
    ///     key (set to null when the job is deleted) with its filtered unique index
    ///     <c>UX_OutgoingEmails_JobId</c>, and its table type over every column. The attachments' table
    ///     takes its key to the email, its index and its blob reference from the conventions alone.
    /// </remarks>
    internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmail>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<OutgoingEmail> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The server-owned columns an insert leaves to the database ---
            _ = builder.Property(static email => email.AttemptCount).HasDefaultValue(0);
            _ = builder.Property(static email => email.ExpectsDeliveryEvents).HasDefaultValue(false);

            // --- The addressed member, whichever user leaf the distribution registers ---
            // No cascade: a user is never deleted once past New, and deleting a New user an email
            // addressed is the pipeline's foreign-key refusal, which is the correct answer.
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The recipient of core.OutgoingEmails");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(OutgoingEmail.RecipientUserId))
                .OnDelete(DeleteBehavior.NoAction);

            // --- The indexes, each the seek of the list beside it ---
            // The administrator's lists by state, oldest first: the stuck and the failed rows.
            _ = builder.HasIndex(static email => new { email.State, email.CreatedAt })
                .HasDatabaseName("IX_OutgoingEmails_State");

            // A business record's emails: the per-document history over the regarding columns.
            _ = builder.HasIndex(static email => new { email.RegardingEntity, email.RegardingId })
                .HasDatabaseName("IX_OutgoingEmails_Regarding")
                .HasFilter("[RegardingEntity] IS NOT NULL");

            // A member's emails, newest first.
            _ = builder.HasIndex(static email => new { email.RecipientUserId, email.CreatedAt })
                .HasDatabaseName("IX_OutgoingEmails_Recipient")
                .IsDescending(false, true)
                .HasFilter("[RecipientUserId] IS NOT NULL");
        }
    }
}
