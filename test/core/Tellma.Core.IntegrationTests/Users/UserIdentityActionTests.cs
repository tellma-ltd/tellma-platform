// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Testing.Support.Identity;

namespace Tellma.Core.IntegrationTests.Users
{
    /// <summary>
    ///     The users' identity actions against a real tenant database and a scripted identity server:
    ///     the invite's write-back and its transitions, the invitation status under the caller's read
    ///     decision, service credentials issued, rotated and revoked, refused when the account changed
    ///     while its client was created, an invite answered in part, a recomposed persist that calls the
    ///     identity server once, an unconfirmed warning that calls it never, the round-trip budgets of
    ///     the users' writes, no transaction open while the identity server is called, and a user delete
    ///     that takes the rows its keys cascade to.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class UserIdentityActionTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IAsyncLifetime
    {
        /// <summary>The scripted identity server the instance calls.</summary>
        private readonly ScriptedIdentityClient _identity = new();

        /// <summary>The membership hints the instance records.</summary>
        private readonly RecordingDirectory _memberships = new();

        /// <summary>The instance over the fixture's databases, with the scripted identity server; set by the initialisation.</summary>
        private ServiceProvider? _instance;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The instance.</summary>
        private ServiceProvider Instance => _instance ?? throw new InvalidOperationException("The instance is not composed yet.");

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            _instance = fixture.CreateInstance(configure: Scripted);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _instance?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        // --- The invite's write-back ---

        [Fact]
        public async Task An_invite_links_and_stamps_each_answered_user_and_moves_only_their_preferences_tags()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int bob = await SeedNewAsync("Bob", lastError: "account_disabled");
            int cal = await SeedNewAsync("Cal");
            (Guid? Permissions, Guid? Preferences) annTags = await fixture.UserTagsAsync(ann);
            (Guid? Permissions, Guid? Preferences) calTags = await fixture.UserTagsAsync(cal);
            Guid? adaPreferences = (await fixture.UserTagsAsync(ada)).Preferences;
            DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

            IReadOnlyList<InviteResult> results = await InviteAsync(admin, ada, bob);

            Assert.Equal([new InviteResult(ada, InviteStatus.Invited, null), new InviteResult(bob, InviteStatus.Invited, null)], results);
            UserRow adaRow = await RowAsync(ada);
            UserRow bobRow = await RowAsync(bob);
            Assert.Equal((ScriptedIdentityClient.SubjectOf(Email(ada)), "Invited", "Invited"), (adaRow.Subject, adaRow.State, adaRow.InviteStatus));
            Assert.Null(adaRow.LastInviteError);

            // A later success clears the last error.
            Assert.Null(bobRow.LastInviteError);
            Assert.True(adaRow.InvitedAt >= before);
            Assert.True(adaRow.ModifiedAt >= before);
            Assert.Equal(ann, adaRow.ModifiedById);
            Assert.NotEqual(adaPreferences, (await fixture.UserTagsAsync(ada)).Preferences);
            Assert.Equal(calTags, await fixture.UserTagsAsync(cal));
            Assert.Equal(annTags, await fixture.UserTagsAsync(ann));
            Assert.Equal([(ConnectDatabase.TenantA, ScriptedIdentityClient.SubjectOf(Email(ada))), (ConnectDatabase.TenantA, ScriptedIdentityClient.SubjectOf(Email(bob)))], _memberships.Hints());

            // The invitations carried the tenant's link and looked nobody up only.
            Assert.All(Assert.Single(_identity.Invites), static invitation => Assert.False(invitation.ExistingOnly));
        }

        [Fact]
        public async Task A_refused_re_invite_keeps_the_link_the_invitation_and_the_status_and_records_the_error()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            _ = await InviteAsync(admin, ada);
            UserRow invited = await RowAsync(ada);
            _identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, null, null, "account_disabled")]);

            IReadOnlyList<InviteResult> results = await InviteAsync(admin, ada);

            Assert.Equal([new InviteResult(ada, null, "account_disabled")], results);
            UserRow refused = await RowAsync(ada);
            Assert.Equal((invited.Subject, invited.InvitedAt, invited.InviteStatus, "Invited"), (refused.Subject, refused.InvitedAt, refused.InviteStatus, refused.State));
            Assert.Equal("account_disabled", refused.LastInviteError);
        }

        [Fact]
        public async Task An_address_now_another_account_s_leaves_the_row_untouched_and_answers_a_mismatch()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            _ = await InviteAsync(admin, ada);
            UserRow invited = await RowAsync(ada);
            _identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, "sub-someone-else", InviteStatus.Active, null)]);

            IReadOnlyList<InviteResult> results = await InviteAsync(admin, ada);

            Assert.Equal([new InviteResult(ada, null, AccessCodes.SubjectMismatch)], results);
            Assert.Equal(invited, await RowAsync(ada));
        }

        [Fact]
        public async Task Two_users_resolving_to_one_account_are_refused_as_a_duplicate_subject()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int twin = await SeedNewAsync("Twin");
            _ = await InviteAsync(admin, ada);
            string taken = (await RowAsync(ada)).Subject!;
            _identity.OnInvite = (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, taken, InviteStatus.Active, null)]);

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => InviteAsync(admin, twin));

            // An action carries no payload row, so the duplicate is reported on the whole payload, naming the property.
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal((ValidationPath.Root, ValidationCodes.Unique), (error.Path, error.Code));
            Assert.Equal(nameof(User.Subject), error.Arguments["property"]);
            Assert.Null((await RowAsync(twin)).Subject);
        }

        [Fact]
        public async Task An_invite_refuses_a_service_account_and_a_joined_user_before_calling_the_identity_server()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => InviteAsync(admin, robot, ann));

            Assert.Equal([AccessCodes.NotIndividual, AccessCodes.AlreadyJoined], refused.Codes());
            Assert.Empty(_identity.Invites);
        }

        [Fact]
        public async Task An_invite_takes_two_round_trips_and_holds_no_transaction_while_the_identity_server_answers()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int bob = await SeedNewAsync("Bob");
            await WarmAsync(admin);
            ConcurrentQueue<int> open = new();
            _identity.OnInvite = async (invitations, cancellationToken) =>
            {
                open.Enqueue(await OpenTransactionsAsync(cancellationToken));
                return [.. invitations.Select(static invitation => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Invited, null))];
            };

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            _ = await scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [ada, bob], null, new ActionOptions(), Token);

            Assert.Equal(2, access.RoundTrips - start);
            Assert.Equal([0], open);
        }

        [Fact]
        public async Task A_persist_recomposed_after_the_identity_server_answered_calls_it_once_and_commits_once()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            await WarmAsync(admin);
            _identity.OnInvite = async (invitations, _) =>
            {
                // The caller's permissions move while the identity server answers, so the persist's guard refuses the first send.
                await fixture.BumpPermissionsAsync(ann);
                return [.. invitations.Select(static invitation => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Invited, null))];
            };

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            IReadOnlyList<InviteResult> results = await scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [ada], null, new ActionOptions(), Token);

            Assert.Equal([new InviteResult(ada, InviteStatus.Invited, null)], results);
            Assert.Equal(3, access.RoundTrips - start);
            Assert.Single(_identity.Invites);
            Assert.Equal("Invited", (await RowAsync(ada)).State);
            Assert.Single(_memberships.Hints());
        }

        [Fact]
        public async Task An_invite_the_identity_server_answered_in_part_commits_the_prefix_and_raises_the_rest_in_two_round_trips()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int bob = await SeedNewAsync("Bob");
            await WarmAsync(admin);
            ConcurrentQueue<int> open = new();
            _identity.OnInvite = async (invitations, cancellationToken) =>
            {
                // The identity server answers the first invitation only, as when it stops part way.
                open.Enqueue(await OpenTransactionsAsync(cancellationToken));
                return [new IdentityInvitationResult(invitations[0].Email, ScriptedIdentityClient.SubjectOf(invitations[0].Email), InviteStatus.Invited, null)];
            };

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            PartialFailureException partial = await Assert.ThrowsAsync<PartialFailureException>(
                () => scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [ada, bob], null, new ActionOptions(), Token));

            Assert.Equal(2, access.RoundTrips - start);
            Assert.Equal([0], open);
            Assert.Equal([new InviteResult(ada, InviteStatus.Invited, null)], Assert.IsAssignableFrom<IReadOnlyList<InviteResult>>(partial.Results));
            Assert.Equal([bob.ToString(CultureInfo.InvariantCulture)], partial.Failed);

            // --- The answered prefix committed; the user never attempted is untouched ---
            UserRow invited = await RowAsync(ada);
            Assert.Equal((ScriptedIdentityClient.SubjectOf(Email(ada)), "Invited"), (invited.Subject, invited.State));
            UserRow untouched = await RowAsync(bob);
            Assert.Null(untouched.Subject);
            Assert.Null(untouched.InvitedAt);
            Assert.Equal("New", untouched.State);
            Assert.Equal([(ConnectDatabase.TenantA, ScriptedIdentityClient.SubjectOf(Email(ada)))], _memberships.Hints());
        }

        // --- The invitation status ---

        [Fact]
        public async Task The_invitation_status_answers_in_request_order_in_one_round_trip_and_asks_for_the_emailed_only()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int bob = await SeedNewAsync("Bob");
            int cal = await SeedNewAsync("Cal");
            _ = await InviteAsync(admin, ada);
            _identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, ScriptedIdentityClient.SubjectOf(invitations[0].Email), InviteStatus.Active, null)]);
            _ = await InviteAsync(admin, bob);
            await WarmAsync(admin);

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            var statuses = (IReadOnlyList<InvitationStatus>)(await InvokeStatusAsync(scope, cal, bob, ada, 0, ada))!;

            Assert.Equal(1, access.RoundTrips - start);
            Assert.Equal([cal, bob, ada], statuses.Select(static status => status.Id));
            Assert.Equal(UserState.New, statuses[0].State);
            Assert.Null(statuses[0].Delivery);
            Assert.Equal(IdentityDeliveryState.NotFound, statuses[1].Delivery?.State);
            Assert.Equal(IdentityDeliveryState.Sent, statuses[2].Delivery?.State);
            Assert.Equal([ScriptedIdentityClient.SubjectOf(Email(ada))], Assert.Single(_identity.Deliveries));
        }

        [Fact]
        public async Task The_invitation_status_omits_the_users_the_caller_cannot_read_and_asks_nothing_for_them()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            _ = await InviteAsync(admin, ada);

            // A member with no role at all reads their own row and nobody else's.
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Invariant($"DELETE FROM [core].[RoleMemberships] WHERE [UserId] = {dee};"));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance);
            var statuses = (IReadOnlyList<InvitationStatus>)(await InvokeStatusAsync(scope, ada, dee))!;

            Assert.Equal([dee], statuses.Select(static status => status.Id));
            Assert.Empty(_identity.Deliveries);
        }

        // --- Service credentials ---

        [Fact]
        public async Task Credentials_link_and_join_the_account_rotation_deletes_the_old_client_and_deactivation_the_current()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            Guid? preferences = (await fixture.UserTagsAsync(robot)).Preferences;

            CredentialsResult first = await IssueAsync(admin, robot);
            UserRow issued = await RowAsync(robot);
            Assert.Equal((first.ClientId, "Joined"), (issued.Subject, issued.State));
            Assert.NotNull(issued.JoinedAt);
            Assert.NotEqual(preferences, (await fixture.UserTagsAsync(robot)).Preferences);
            Assert.Empty(_identity.Deleted);

            CredentialsResult second = await IssueAsync(admin, robot);
            UserRow rotated = await RowAsync(robot);
            Assert.Equal(second.ClientId, rotated.Subject);
            Assert.Equal(issued.JoinedAt, rotated.JoinedAt);
            Assert.Equal([first.ClientId], _identity.Deleted);
            Assert.NotEqual(first.ClientSecret, second.ClientSecret);

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                _ = await scope.Users().ExecuteActionAsync("deactivate", [robot], null, new ActionOptions(), Token);
            }

            Assert.Equal([first.ClientId, second.ClientId], _identity.Deleted);
            Assert.Equal((ConnectDatabase.TenantA, second.ClientId, false), _memberships.Records()[^1]);
        }

        [Fact]
        public async Task Credentials_take_two_round_trips_and_refuse_an_individual_before_creating_a_client()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            await WarmAsync(admin);

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int start = access.RoundTrips;
                _ = await scope.Users().ExecuteActionAsync<CredentialsResult>("issue-credentials", [robot], null, new ActionOptions(), Token);
                Assert.Equal(2, access.RoundTrips - start);
            }

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => IssueAsync(admin, ann));
            Assert.Equal([AccessCodes.NotService], refused.Codes());
            Assert.Single(_identity.Created);
        }

        [Fact]
        public async Task Credentials_hold_no_transaction_while_the_client_is_created()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            ConcurrentQueue<int> open = new();
            _identity.BeforeCreate = async (_, cancellationToken) => open.Enqueue(await OpenTransactionsAsync(cancellationToken));

            _ = await IssueAsync(admin, robot);

            Assert.Equal([0], open);
        }

        [Fact]
        public async Task A_credentials_persist_recomposed_after_the_client_was_created_creates_one_and_records_it_once()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            await WarmAsync(admin);

            // The caller's permissions move while the identity server creates the client, so the persist's guard refuses the first send.
            _identity.BeforeCreate = (_, _) => fixture.BumpPermissionsAsync(ann);
            CredentialsResult first;
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int start = access.RoundTrips;
                first = await scope.Users().ExecuteActionAsync<CredentialsResult>("issue-credentials", [robot], null, new ActionOptions(), Token);
                Assert.Equal(3, access.RoundTrips - start);
            }

            Assert.Equal(first.ClientId, Assert.Single(_identity.Created).Account.ClientId);
            Assert.Equal(first.ClientId, (await RowAsync(robot)).Subject);
            Assert.Equal([(ConnectDatabase.TenantA, first.ClientId, true)], _memberships.Records());
            Assert.Empty(_identity.Deleted);

            // --- A recomposed rotation deletes the previous client once, and never the new one ---
            CredentialsResult second = await IssueAsync(admin, robot);

            Assert.Equal(2, _identity.Created.Count);
            Assert.Equal(second.ClientId, (await RowAsync(robot)).Subject);
            Assert.Equal([first.ClientId], _identity.Deleted);
            Assert.Equal([(ConnectDatabase.TenantA, first.ClientId, true), (ConnectDatabase.TenantA, second.ClientId, true)], _memberships.Records());
        }

        [Fact]
        public async Task Credentials_on_an_account_whose_client_changed_meanwhile_are_refused_and_the_new_client_deleted()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            CredentialsResult first = await IssueAsync(admin, robot);

            // Another issue links the account to another client while this one's client is created.
            _identity.BeforeCreate = (_, _) => fixture.ExecuteAsync(ConnectDatabase.TenantA, Invariant($"UPDATE [core].[Users] SET [Subject] = N'svc-concurrent' WHERE [Id] = {robot};"));

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => IssueAsync(admin, robot));

            Assert.Equal([AccessCodes.CredentialsConcurrent], refused.Codes());
            Assert.Equal("svc-concurrent", (await RowAsync(robot)).Subject);
            Assert.Equal([_identity.Created.Last().Account.ClientId], _identity.Deleted);
            Assert.DoesNotContain(_identity.Deleted, deleted => deleted == first.ClientId);
        }

        [Fact]
        public async Task Credentials_on_an_account_deactivated_meanwhile_are_refused_and_the_new_client_deleted()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int robot = await SeedServiceAsync();
            _identity.BeforeCreate = (_, _) => fixture.ExecuteAsync(ConnectDatabase.TenantA, Invariant($"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {robot};"));

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => IssueAsync(admin, robot));

            Assert.Equal([AccessCodes.Inactive], refused.Codes());
            UserRow row = await RowAsync(robot);
            Assert.Null(row.Subject);
            Assert.Null(row.JoinedAt);
            Assert.Equal([Assert.Single(_identity.Created).Account.ClientId], _identity.Deleted);
            Assert.Empty(_memberships.Records());
        }

        // --- A warning the caller did not confirm refuses both identity actions before the identity server is called ---

        [Fact]
        public async Task An_unconfirmed_warning_calls_no_identity_server_and_the_confirmed_resend_invites_once()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int robot = await SeedServiceAsync();
            UserRow uninvited = await RowAsync(ada);
            await using ServiceProvider warned = fixture.CreateInstance(configure: Scripted, compose: static tellma => tellma.AddFeature(new IdentityWarningFeature()));

            // --- Neither action calls out while the warning stands ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, warned))
            {
                ConfirmationRequiredException invite = await Assert.ThrowsAsync<ConfirmationRequiredException>(
                    () => scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [ada], null, new ActionOptions(), Token));
                Assert.Equal([IdentityWarningFeature.Key], invite.Requests.Select(static request => request.Key));
            }

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, warned))
            {
                _ = await Assert.ThrowsAsync<ConfirmationRequiredException>(
                    () => scope.Users().ExecuteActionAsync<CredentialsResult>("issue-credentials", [robot], null, new ActionOptions(), Token));
            }

            Assert.Empty(_identity.Invites);
            Assert.Empty(_identity.Created);
            Assert.Equal(uninvited, await RowAsync(ada));
            Assert.Null((await RowAsync(robot)).Subject);

            // --- The confirmed resend invites once ---
            IReadOnlyList<InviteResult> results;
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, warned))
            {
                scope.ServiceProvider.GetRequiredService<Confirmations>().Seed([IdentityWarningFeature.Key]);
                results = await scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", [ada], null, new ActionOptions(), Token);
            }

            Assert.Equal([new InviteResult(ada, InviteStatus.Invited, null)], results);
            Assert.Single(_identity.Invites);
        }

        // --- The probe the no-open-transaction tests rest on ---

        [Fact]
        public async Task The_open_transaction_probe_sees_another_session_s_open_transaction()
        {
            int ada = await SeedNewAsync("Ada");
            Assert.Equal(0, await OpenTransactionsAsync(Token));

            // A transaction holding a write on another session, as the pipeline's would.
            await using SqlConnection connection = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await connection.OpenAsync(Token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Invariant($"UPDATE [core].[Users] SET [LastInviteError] = N'probe' WHERE [Id] = {ada};");
                Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
            }

            Assert.Equal(1, await OpenTransactionsAsync(Token));
            await transaction.RollbackAsync(Token);
            Assert.Equal(0, await OpenTransactionsAsync(Token));
        }

        // --- The budgets of the users' other writes ---

        [Fact]
        public async Task An_admin_save_that_changes_a_locale_member_takes_two_round_trips()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            await WarmAsync(admin);

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            User row = await scope.LoadUserAsync(ada);
            row.PreferredTimeZone = "Asia/Riyadh";
            row.RoleMemberships = null;
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            _ = await scope.SaveUsersAsync(row);

            Assert.Equal(2, access.RoundTrips - start);
        }

        [Fact]
        public async Task A_deactivation_and_an_activation_take_two_round_trips_each()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            await WarmAsync(admin);

            Assert.Equal(2, await ActionRoundTripsAsync(admin, "deactivate", ada));
            Assert.Equal(2, await ActionRoundTripsAsync(admin, "activate", ada));
        }

        [Fact]
        public async Task A_user_delete_takes_two_round_trips()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            await WarmAsync(admin);

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            _ = await scope.Users().DeleteByIdsAsync([ada], cancellationToken: Token);

            Assert.Equal(2, access.RoundTrips - start);
        }

        [Fact]
        public async Task A_user_s_kind_is_written_once()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");

            // An individual turned into a service account: Individual is the kind's default, which
            // an update reads as "not sent", so the change is tested in this direction.
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance);
            User row = await scope.LoadUserAsync(ada);
            row.Kind = UserKind.Service;
            row.RoleMemberships = null;

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.SaveUsersAsync(row));
            Assert.Contains(ValidationCodes.WriteOnce, refused.Codes());
        }

        // --- A delete: the rows the users' keys cascade to leave with them, and the batch declares them ---

        [Fact]
        public async Task A_user_delete_takes_its_stamp_and_preferences_with_it()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            int ada = await SeedNewAsync("Ada");
            int bob = await SeedNewAsync("Bob");
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                Invariant(
                    $"""
                    INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) VALUES ({ada}, 'nav.pinned', N'[]'), ({bob}, 'nav.pinned', N'[]');
                    INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled]) VALUES ({ada}, 'core.test', 'Email', 0), ({bob}, 'core.test', 'Email', 0);
                    """));

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                AffectedResult deleted = await scope.Users().DeleteByIdsAsync([ada], cancellationToken: Token);
                Assert.Equal(1, deleted.Count);
            }

            Assert.Equal((0, 0, 0, 0), await DependantsAsync(ada));
            Assert.Equal((1, 1, 1, 1), await DependantsAsync(bob));
        }

        // --- Helpers ---

        /// <summary>Replaces the identity server and the membership directory with the test's scripted ones.</summary>
        /// <param name="services">The instance's services.</param>
        private void Scripted(IServiceCollection services)
        {
            _ = services.Replace(ServiceDescriptor.Singleton<IIdentityServerClient>(_identity));
            _ = services.Replace(ServiceDescriptor.Singleton<ITenantMembershipDirectory>(_memberships));
        }

        /// <summary>Invites users in a stepped-up scope of the caller.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="ids">The users.</param>
        /// <returns>The results.</returns>
        private async Task<IReadOnlyList<InviteResult>> InviteAsync(string subject, params int[] ids)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, Instance);
            return await scope.Users().ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", ids, null, new ActionOptions(), Token);
        }

        /// <summary>Issues a service account's credentials in a stepped-up scope of the caller.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="id">The account.</param>
        /// <returns>The credentials.</returns>
        private async Task<CredentialsResult> IssueAsync(string subject, int id)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, Instance);
            return await scope.Users().ExecuteActionAsync<CredentialsResult>("issue-credentials", [id], null, new ActionOptions(), Token);
        }

        /// <summary>Runs a built-in action over one user in a stepped-up scope of the caller and counts its round trips.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="action">The action.</param>
        /// <param name="id">The user.</param>
        /// <returns>The action's round trips.</returns>
        private async Task<int> ActionRoundTripsAsync(string subject, string action, int id)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            _ = await scope.Users().ExecuteActionAsync(action, [id], null, new ActionOptions(), Token);
            return access.RoundTrips - start;
        }

        /// <summary>Reads the invitation status through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="ids">The users asked.</param>
        /// <returns>The answer.</returns>
        private static Task<object?> InvokeStatusAsync(AsyncServiceScope scope, params long[] ids)
        {
            StackDescriptor users = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Find(CoreResources.User)!;
            ApiActionDescriptor status = users.Actions.OfType<ApiActionDescriptor>().Single(static action => action.Name == "invitation-status");
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(status, new IdsRequest(ids), Token);
        }

        /// <summary>Reads the users once as the caller, which warms the caller's connection and permissions.</summary>
        /// <param name="subject">The caller.</param>
        /// <returns>A task that completes when the read ran.</returns>
        private async Task WarmAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, Instance);
            _ = await scope.Users().QueryAsync(new QueryRequest { Select = "Id" }, Token);
        }

        /// <summary>Seeds an active individual of the first tenant who has never been invited.</summary>
        /// <param name="name">The user's name.</param>
        /// <param name="lastError">The last invitation's error; null for none.</param>
        /// <returns>The user's id.</returns>
        private async Task<int> SeedNewAsync(string name, string? lastError = null)
        {
            int id = fixture.NextUserId();
            string error = lastError is null ? "NULL" : $"N'{lastError}'";
            await SeedRowAsync(id, "Individual", $"N'{Email(id)}'", name, error);
            return id;
        }

        /// <summary>Seeds an active service account of the first tenant with no client yet.</summary>
        /// <returns>The account's id.</returns>
        private async Task<int> SeedServiceAsync()
        {
            int id = fixture.NextUserId();
            await SeedRowAsync(id, "Service", "NULL", "Robot", "NULL");
            return id;
        }

        /// <summary>Seeds an active, unlinked user row with its stamp.</summary>
        /// <param name="id">The id.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="email">The email as a SQL literal.</param>
        /// <param name="name">The name.</param>
        /// <param name="lastError">The last invitation error as a SQL literal.</param>
        /// <returns>A task that completes when the row is seeded.</returns>
        private Task SeedRowAsync(int id, string kind, string email, string name, string lastError)
        {
            int system = WellKnownIds.SystemUserId;
            return fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                Invariant(
                    $"""
                    INSERT INTO [core].[Users] ([Id], [Kind], [Email], [Name], [LastInviteError], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                    VALUES ({id}, '{kind}', {email}, N'{name}', {lastError}, 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                    INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                    """));
        }

        /// <summary>A seeded individual's email.</summary>
        /// <param name="id">The user's id.</param>
        /// <returns>The email.</returns>
        private static string Email(int id)
        {
            return Invariant($"user{id}@example.com");
        }

        /// <summary>Reads the members of a user row the identity actions write.</summary>
        /// <param name="id">The user.</param>
        /// <returns>The row.</returns>
        private async Task<UserRow> RowAsync(int id)
        {
            await using SqlConnection connection = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = Invariant(
                $"SELECT [Subject], [State], [InviteStatus], [LastInviteError], [InvitedAt], [JoinedAt], [ModifiedAt], [ModifiedById] FROM [core].[Users] WHERE [Id] = {id};");
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            return new UserRow(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4),
                reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
                reader.GetDateTimeOffset(6),
                reader.GetInt32(7));
        }

        /// <summary>Counts a user's row and the rows that reference it through a cascading key.</summary>
        /// <param name="id">The user.</param>
        /// <returns>The user rows, stamps, preferences and notification preferences.</returns>
        private async Task<(int Users, int Stamps, int Preferences, int NotificationPreferences)> DependantsAsync(int id)
        {
            await using SqlConnection connection = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = Invariant(
                $"""
                SELECT (SELECT COUNT(*) FROM [core].[Users] WHERE [Id] = {id}),
                       (SELECT COUNT(*) FROM [core].[UserStamps] WHERE [UserId] = {id}),
                       (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = {id}),
                       (SELECT COUNT(*) FROM [core].[NotificationPreferences] WHERE [UserId] = {id});
                """);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
        }

        /// <summary>The user sessions of the first tenant's database, other than the asking one, with a transaction open; the server's own background tasks are not counted.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The count.</returns>
        private async Task<int> OpenTransactionsAsync(CancellationToken cancellationToken)
        {
            await using SqlConnection connection = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE [database_id] = DB_ID() AND [is_user_process] = 1 AND [open_transaction_count] > 0 AND [session_id] <> @@SPID;";
            return (int)await command.ExecuteScalarAsync(cancellationToken);
        }

        /// <summary>Formats with the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The members of a user row the identity actions write.</summary>
        /// <param name="Subject">The linked account.</param>
        /// <param name="State">The computed state.</param>
        /// <param name="InviteStatus">The last successful invitation's status.</param>
        /// <param name="LastInviteError">The last invitation's error.</param>
        /// <param name="InvitedAt">When the user was last invited.</param>
        /// <param name="JoinedAt">When the user joined.</param>
        /// <param name="ModifiedAt">The row's stamp.</param>
        /// <param name="ModifiedById">Who stamped it.</param>
        private sealed record UserRow(
            string? Subject, string State, string? InviteStatus, string? LastInviteError, DateTimeOffset? InvitedAt, DateTimeOffset? JoinedAt, DateTimeOffset ModifiedAt, int ModifiedById);

        /// <summary>A membership directory that records every hint and lists nothing.</summary>
        private sealed class RecordingDirectory : ITenantMembershipDirectory
        {
            /// <summary>Every hint, in call order.</summary>
            private readonly ConcurrentQueue<TenantMembershipRecord> _recorded = new();

            /// <summary>The hints recorded, each as its tenant and subject.</summary>
            /// <returns>The hints.</returns>
            public IReadOnlyList<(int TenantId, string Subject)> Hints()
            {
                return [.. _recorded.Select(static record => (record.TenantId, record.Subject))];
            }

            /// <summary>The hints recorded, each as its tenant, subject and activity.</summary>
            /// <returns>The hints.</returns>
            public IReadOnlyList<(int TenantId, string Subject, bool IsActive)> Records()
            {
                return [.. _recorded.Select(static record => (record.TenantId, record.Subject, record.IsActive))];
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<TenantMembership>>([]);
            }

            /// <inheritdoc />
            public Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(records);
                foreach (TenantMembershipRecord record in records)
                {
                    _recorded.Enqueue(record);
                }

                return Task.CompletedTask;
            }
        }
    }

    /// <summary>A feature whose users' validator raises a warning on both identity actions.</summary>
    internal sealed class IdentityWarningFeature : ITellmaFeature
    {
        /// <summary>The warning's confirmation key.</summary>
        public const string Key = "Test.IdentityChecked";

        /// <inheritdoc />
        public string Name => "test";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution.Confirmation(Key).Validator<User, IdentityWarningValidator>();
        }
    }

    /// <summary>A users' validator that asks the caller to confirm every invite and every issue of credentials.</summary>
    internal sealed class IdentityWarningValidator : IEntityValidator<User>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<User> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ValidateActionAsync(string action, ActionContext<User> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (action is "invite" or "issue-credentials")
            {
                context.Host.Confirmations.Require(IdentityWarningFeature.Key);
            }

            return Task.CompletedTask;
        }
    }
}
