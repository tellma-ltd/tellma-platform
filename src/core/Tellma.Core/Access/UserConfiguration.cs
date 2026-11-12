// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The model of <c>core.Users</c> beyond what the annotations and conventions give it: the
    ///     computed state, the subject's covering index, the default list's index, the kind's default,
    ///     the checks that keep the evidence in step with the kind, and the system user's row. Closed
    ///     over the distribution's leaf.
    /// </summary>
    /// <typeparam name="TUser">The leaf registered for <see cref="User" />.</typeparam>
    internal sealed class UserConfiguration<TUser> : IEntityTypeConfiguration<TUser>
        where TUser : User
    {
        /// <summary>
        ///     The state, computed from the evidence and stored. The cast and the <c>ISNULL</c> make the
        ///     column <c>varchar(8) NOT NULL</c>, which a bare <c>CASE</c> would not be.
        /// </summary>
        public const string StateSql =
            "ISNULL(CAST(CASE WHEN [JoinedAt] IS NOT NULL THEN 'Joined' WHEN [InvitedAt] IS NOT NULL THEN 'Invited' ELSE 'New' END AS varchar(8)), 'New')";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TUser> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Columns the annotations cannot express ---
            _ = builder.Property(static user => user.Kind).HasDefaultValue(UserKind.Individual);
            _ = builder.Property(static user => user.State).HasComputedColumnSql(StateSql, stored: true);

            // --- Indexes: the connect prologue's covering seek by subject, and the default list ---
            // spec 0014 §3.1: the seek reads the kind, the activity flag and the state without a lookup.
            _ = builder.HasIndex(static user => user.Subject)
                .HasDatabaseName("UX_Users_Subject")
                .IsUnique()
                .HasFilter("[Subject] IS NOT NULL")
                .IncludeProperties(static user => new { user.Kind, user.IsActive, user.State });
            _ = builder.HasIndex(static user => new { user.IsActive, user.Name })
                .HasDatabaseName("IX_Users_IsActive_Name");

            // --- Checks: the email and the evidence agree with the kind ---
            string individual = nameof(UserKind.Individual);
            string system = nameof(UserKind.System);
            string service = nameof(UserKind.Service);
            _ = builder.ToTable(table =>
            {
                // An individual signs in with an email address; no other kind has one.
                _ = table.HasCheckConstraint(
                    "CK_Users_KindEmail",
                    $"([Kind] = '{individual}' AND [Email] IS NOT NULL) OR ([Kind] <> '{individual}' AND [Email] IS NULL)");

                // The system user is row 1 alone, and it can never authenticate.
                _ = table.HasCheckConstraint("CK_Users_SystemIsRowOne", $"[Kind] <> '{system}' OR [Id] = {WellKnownIds.SystemUserId}");
                _ = table.HasCheckConstraint("CK_Users_SystemHasNoSubject", $"[Kind] <> '{system}' OR [Subject] IS NULL");

                // An individual gains a subject when invited, and joins only after an invitation.
                _ = table.HasCheckConstraint(
                    "CK_Users_IndividualEvidence",
                    $"[Kind] <> '{individual}' OR ((([Subject] IS NULL AND [InvitedAt] IS NULL) OR ([Subject] IS NOT NULL AND [InvitedAt] IS NOT NULL)) AND ([JoinedAt] IS NULL OR [InvitedAt] IS NOT NULL))");

                // A service account is never invited, and holds a subject exactly when it has joined.
                _ = table.HasCheckConstraint(
                    "CK_Users_ServiceEvidence",
                    $"[Kind] <> '{service}' OR ([InvitedAt] IS NULL AND [InviteStatus] IS NULL AND (([Subject] IS NULL AND [JoinedAt] IS NULL) OR ([Subject] IS NOT NULL AND [JoinedAt] IS NOT NULL)))");
            });

            // --- The system user, the author of every seeded row, referencing itself ---
            // spec 0014 §3.8: joined (no subject, so it can never sign in) and active.
            _ = builder.HasData(new
            {
                Id = WellKnownIds.SystemUserId,
                Kind = UserKind.System,
                Name = AccessSeeds.SystemUserName,
                JoinedAt = (DateTimeOffset?)AccessSeeds.SeededAt,
                IsActive = true,
                CreatedAt = AccessSeeds.SeededAt,
                CreatedById = WellKnownIds.SystemUserId,
                ModifiedAt = AccessSeeds.SeededAt,
                ModifiedById = WellKnownIds.SystemUserId,
            });
        }
    }
}
