// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;

namespace Tellma.Core.IntegrationTests.Sharing
{
    /// <summary>
    ///     The stored reports' stack and its sharing model against a real tenant database: each
    ///     criterion and the reach of each kind of grant (a user, a role through a current, an
    ///     expired or a future membership, a public or an inactive role, everyone); Edit saves and
    ///     View does not; the Everyone grant's uniqueness and View-only check; standard rows share and
    ///     publish but refuse every other write; a share never stamps its row; a delete removes the
    ///     row's grants and its notices; one statement text serves every caller; the shared-with-me
    ///     filter; the permissions and shares extras, which name no grantee to a caller who may not
    ///     share; the share notice's raise, withdrawal, replacement and multilingual arguments; the
    ///     user and role caps, held under concurrent shares; and the grants' shape.
    ///     The class has its own databases, so it alone seeds their users, roles and reports.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class ReportSharingTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The reports' resource.</summary>
        private const string Resource = "core.Report";

        /// <summary>The share notice's type.</summary>
        private const string Notice = "core.report.shared";

        /// <summary>A list of the users' names, the document every report here holds.</summary>
        private const string Definition = /*lang=json,strict*/ """{ "formatVersion": 1, "list": { "columns": [ { "path": "Name" } ] } }""";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The criteria and the reach of each grant ---

        [Fact]
        public async Task A_new_report_is_its_creators_who_alone_reads_saves_deactivates_and_deletes_it()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner one");
            (_, string otherSubject) = await fixture.SeedCallerAsync("Other one");

            int id = await CreateAsync(ownerSubject, title: "");

            // --- Stamped with its owner and its default title ---
            Assert.Equal((owner, "Users – list"), (await ScalarAsync<int>($"SELECT [OwnerId] FROM [core].[Reports] WHERE [Id] = {id}"), await ScalarAsync<string>($"SELECT [Title] FROM [core].[Reports] WHERE [Id] = {id}")));

            // --- The owner reads and saves; another member sees nothing ---
            Assert.Equal([id], await VisibleAsync(ownerSubject, id));
            Assert.Empty(await VisibleAsync(otherSubject, id));
            await RenameAsync(ownerSubject, id, "Renamed");
            _ = await Assert.ThrowsAsync<NotFoundException>(() => RenameAsync(otherSubject, id, "Taken"));

            // --- The owner deactivates and deletes ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(ownerSubject))
            {
                _ = await Reports(scope).DeactivateAsync([id], cancellationToken: Token);
                _ = await Reports(scope).DeleteByIdsAsync([id], null, Token);
            }

            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[Reports] WHERE [Id] = {id}"));
        }

        [Fact]
        public async Task A_view_grant_reads_without_saving_and_an_edit_grant_saves_without_deleting_or_sharing()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner two");
            (int viewer, string viewerSubject) = await fixture.SeedCallerAsync("Viewer two");
            (int editor, string editorSubject) = await fixture.SeedCallerAsync("Editor two");
            int id = await CreateAsync(ownerSubject);

            await ShareAsync(ownerSubject, [id], new ShareArguments([new UserGrant(viewer, ShareAccess.View), new UserGrant(editor, ShareAccess.Edit)], null));

            // --- Both read; View cannot save, Edit can ---
            Assert.Equal([id], await VisibleAsync(viewerSubject, id));
            Assert.Equal([id], await VisibleAsync(editorSubject, id));
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RenameAsync(viewerSubject, id, "By viewer"));
            Assert.Equal((AccessActions.Save, ForbiddenReason.OutsideFilter), (refused.Action, refused.Reason));
            await RenameAsync(editorSubject, id, "By editor");
            Assert.Equal("By editor", await ScalarAsync<string>($"SELECT [Title] FROM [core].[Reports] WHERE [Id] = {id}"));
            Assert.Equal(owner, await ScalarAsync<int>($"SELECT [OwnerId] FROM [core].[Reports] WHERE [Id] = {id}"));

            // --- Edit confers neither delete nor re-sharing ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(editorSubject))
            {
                ForbiddenException delete = await Assert.ThrowsAsync<ForbiddenException>(() => Reports(scope).DeleteByIdsAsync([id], null, Token));
                Assert.Equal(AccessActions.Delete, delete.Action);
            }

            ForbiddenException share = await Assert.ThrowsAsync<ForbiddenException>(() => ShareAsync(editorSubject, [id], new ShareArguments([new UserGrant(viewer, ShareAccess.Edit)], null)));
            Assert.Equal(AccessActions.Share, share.Action);
        }

        [Fact]
        public async Task A_role_grant_reaches_its_current_members_alone_and_an_inactive_role_reaches_no_one()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner three");
            (int current, string currentSubject) = await fixture.SeedCallerAsync("Current three");
            (int expired, string expiredSubject) = await fixture.SeedCallerAsync("Expired three");
            (int future, string futureSubject) = await fixture.SeedCallerAsync("Future three");
            int role = await fixture.SeedRoleAsync();
            await fixture.SeedMembershipAsync(current, role, startsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())", endsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(expired, role, endsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(future, role, startsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            string ownerPublishes = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(ownerPublishes);

            await PublishAsync(ownerPublishes, [id], new PublishArguments([new RoleGrant(role, ShareAccess.View)], null, null));

            Assert.Equal([id], await VisibleAsync(currentSubject, id));
            Assert.Empty(await VisibleAsync(expiredSubject, id));
            Assert.Empty(await VisibleAsync(futureSubject, id));

            // --- A deactivated role grants nothing, even to its current member ---
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsActive] = 0 WHERE [Id] = {role};");
            Assert.Empty(await VisibleAsync(currentSubject, id));
        }

        [Fact]
        public async Task A_public_role_grant_reaches_every_member()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner four");
            (_, string strangerSubject) = await fixture.SeedCallerAsync("Stranger four");
            int role = await fixture.SeedRoleAsync();
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {role};");
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);

            await PublishAsync(publisher, [id], new PublishArguments([new RoleGrant(role, ShareAccess.View)], null, null));

            Assert.Equal([id], await VisibleAsync(strangerSubject, id));
        }

        [Fact]
        public async Task The_everyone_grant_reaches_every_member_once_and_only_to_view()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner five");
            (_, string strangerSubject) = await fixture.SeedCallerAsync("Stranger five");
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);

            // --- Published to everyone twice: one grant, read by any member ---
            await PublishAsync(publisher, [id], new PublishArguments(null, null, Everyone: true));
            await PublishAsync(publisher, [id], new PublishArguments(null, null, Everyone: true));
            Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id} AND [UserId] IS NULL AND [RoleId] IS NULL"));
            Assert.Equal([id], await VisibleAsync(strangerSubject, id));
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RenameAsync(strangerSubject, id, "By stranger"));
            Assert.Equal(AccessActions.Save, refused.Action);

            // --- The table refuses a second Everyone grant, and one that edits ---
            SqlException twice = await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteAsync(ConnectDatabase.TenantA, EveryoneGrant(id, ShareAccess.View)));
            Assert.Contains("UX_ReportShares_Everyone", twice.Message, StringComparison.Ordinal);
            int other = await CreateAsync(publisher);
            SqlException edits = await Assert.ThrowsAsync<SqlException>(() => fixture.ExecuteAsync(ConnectDatabase.TenantA, EveryoneGrant(other, ShareAccess.Edit)));
            Assert.Contains("CK_ReportShares_EveryoneView", edits.Message, StringComparison.Ordinal);

            // --- Withdrawn: no one but the owner reads it again ---
            await PublishAsync(publisher, [id], new PublishArguments(null, null, Everyone: false));
            Assert.Empty(await VisibleAsync(strangerSubject, id));
        }

        [Fact]
        public async Task One_statement_text_reads_the_reports_for_every_caller()
        {
            (_, string first) = await fixture.SeedCallerAsync("Text one");
            (_, string second) = await fixture.SeedCallerAsync("Text two");
            _ = await CreateAsync(first);
            _ = await CreateAsync(second);
            await VisibleAsync(first);
            await VisibleAsync(second);

            // --- Warm, each caller's query captured: the same text, every user bound as a value ---
            IReadOnlyList<string> firstTexts = await CapturedAsync(() => VisibleAsync(first));
            IReadOnlyList<string> secondTexts = await CapturedAsync(() => VisibleAsync(second));

            string text = Assert.Single(firstTexts);
            Assert.Contains("[core].[ReportShares]", text, StringComparison.Ordinal);
            Assert.Equal(text, Assert.Single(secondTexts));
        }

        // --- Standard rows ---

        [Fact]
        public async Task A_standard_report_shares_and_publishes_but_refuses_save_delete_deactivate_and_transfer()
        {
            (int member, string memberSubject) = await fixture.SeedCallerAsync("Member six");
            (int admin, string adminSubject) = await fixture.SeedCallerAsync("Admin six");
            int role = await fixture.SeedRoleAsync(
                (Resource, AccessActions.Read, null),
                (Resource, AccessActions.Save, null),
                (Resource, AccessActions.Delete, null),
                (Resource, AccessActions.Activate, null),
                (Resource, AccessActions.Share, null),
                (Resource, AccessActions.Publish, null),
                (Resource, AccessActions.Transfer, null));
            await fixture.SeedMembershipAsync(admin, role);
            int id = await SeedStandardAsync("Standard six");

            // --- Shared and published like any other ---
            await ShareAsync(adminSubject, [id], new ShareArguments([new UserGrant(member, ShareAccess.View)], null));
            await PublishAsync(adminSubject, [id], new PublishArguments(null, null, Everyone: true));
            Assert.Equal([id], await VisibleAsync(memberSubject, id));

            // --- Every other write refused, at the row or its target ---
            ValidationException save = await Assert.ThrowsAsync<ValidationException>(() => RenameAsync(adminSubject, id, "Changed"));
            Assert.Equal(("[0]", SharingValidationCodes.StandardReadOnly), Only(save));
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(adminSubject))
            {
                Assert.Equal(("Ids[0]", SharingValidationCodes.StandardReadOnly), Only(await Assert.ThrowsAsync<ValidationException>(() => Reports(scope).DeleteByIdsAsync([id], null, Token))));
            }

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(adminSubject))
            {
                Assert.Equal(("Ids[0]", SharingValidationCodes.StandardReadOnly), Only(await Assert.ThrowsAsync<ValidationException>(() => Reports(scope).DeactivateAsync([id], cancellationToken: Token))));
            }

            ValidationException transfer = await Assert.ThrowsAsync<ValidationException>(() => TransferAsync(adminSubject, [id], new TransferArguments(admin)));
            Assert.Equal(("Ids[0]", SharingValidationCodes.StandardReadOnly), Only(transfer));
            Assert.Equal(("Standard six", true, null), (await ScalarAsync<string>($"SELECT [Title] FROM [core].[Reports] WHERE [Id] = {id}"), await ScalarAsync<bool>($"SELECT [IsActive] FROM [core].[Reports] WHERE [Id] = {id}"), await ScalarAsync<int?>($"SELECT [OwnerId] FROM [core].[Reports] WHERE [Id] = {id}")));

            // --- The permissions extra says so per ability ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(adminSubject))
            {
                EntitiesResult<Report> read = await Reports(scope).GetByIdAsync(id, new DetailsRequest { Include = ["permissions"] }, Token);
                JsonElement refused = Assert.Single(read.Extras!["permissions"].EnumerateArray()).GetProperty("refused");
                Assert.Equal(
                    ["action:transfer", "activate", "delete", "save"],
                    refused.EnumerateObject().Where(static ability => ability.Value.GetString() == SharingValidationCodes.StandardReadOnly).Select(static ability => ability.Name).Order(StringComparer.Ordinal));
                Assert.False(refused.TryGetProperty("action:share", out _));
                Assert.False(refused.TryGetProperty("action:publish", out _));
            }
        }

        // --- What a share writes, and what a delete removes ---

        [Fact]
        public async Task A_share_never_stamps_its_row_a_transfer_does_and_a_delete_removes_the_rows_grants_and_notices()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner seven");
            (int grantee, _) = await fixture.SeedCallerAsync("Grantee seven");
            (int heir, string heirSubject) = await fixture.SeedCallerAsync("Heir seven");
            string publisher = await PublisherAsync(ownerSubject, AccessActions.Transfer);
            int id = await CreateAsync(publisher);
            string stamp = $"SELECT CONVERT(nvarchar(40), [ModifiedAt], 127) FROM [core].[Reports] WHERE [Id] = {id}";
            string? before = await ScalarAsync<string>(stamp);

            // --- Sharing and publishing write grants beside the row, never the row ---
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.View), new UserGrant(heir, ShareAccess.Edit)], null));
            await PublishAsync(publisher, [id], new PublishArguments(null, null, Everyone: true));
            Assert.Equal(before, await ScalarAsync<string>(stamp));
            Assert.Equal(3, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));

            // --- A transfer stamps the row and drops the new owner's own grant ---
            await TransferAsync(publisher, [id], new TransferArguments(heir));
            Assert.NotEqual(before, await ScalarAsync<string>(stamp));
            Assert.Equal(heir, await ScalarAsync<int>($"SELECT [OwnerId] FROM [core].[Reports] WHERE [Id] = {id}"));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id} AND [UserId] = {heir}"));

            // --- The remaining grantee still holds the share notice, the new owner no longer does ---
            string notices = $"SELECT COUNT(*) FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'";
            Assert.Equal(grantee, await ScalarAsync<int>($"SELECT [UserId] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'"));
            Assert.Equal(1, await ScalarAsync<int>(notices));

            // --- The new owner deletes it, and its grants and notices go with it ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(heirSubject))
            {
                _ = await Reports(scope).DeleteByIdsAsync([id], null, Token);
            }

            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));
            Assert.Equal(0, await ScalarAsync<int>(notices));
        }

        [Fact]
        public async Task The_sharing_actions_refuse_inactive_grantees_the_owner_and_an_inactive_new_owner()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner eight");
            (int inactive, _) = await fixture.SeedCallerAsync("Inactive eight");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {inactive};");
            int role = await fixture.SeedRoleAsync();
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsActive] = 0 WHERE [Id] = {role};");
            string publisher = await PublisherAsync(ownerSubject, AccessActions.Transfer);
            int id = await CreateAsync(publisher);

            ValidationException share = await Assert.ThrowsAsync<ValidationException>(() => ShareAsync(publisher, [id], new ShareArguments([new UserGrant(inactive, ShareAccess.View), new UserGrant(owner, ShareAccess.View)], null)));
            Assert.Equal(
                [("Arguments.Set[0].UserId", SharingValidationCodes.UserInvalid), ("Arguments.Set[1].UserId", SharingValidationCodes.OwnerIsImplicit)],
                share.Errors.Select(static error => (error.Path.ToString(), error.Code)));
            ValidationException publish = await Assert.ThrowsAsync<ValidationException>(() => PublishAsync(publisher, [id], new PublishArguments([new RoleGrant(role, ShareAccess.View)], null, null)));
            Assert.Equal(("Arguments.Set[0].RoleId", SharingValidationCodes.RoleInvalid), Only(publish));
            ValidationException transfer = await Assert.ThrowsAsync<ValidationException>(() => TransferAsync(publisher, [id], new TransferArguments(inactive)));
            Assert.Equal(("Arguments.OwnerId", SharingValidationCodes.OwnerInvalid), Only(transfer));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));
        }

        // --- The reports page's tab and the share dialog's extras ---

        [Fact]
        public async Task Shared_with_me_lists_the_reports_others_own_or_no_one_owns_that_reach_the_caller()
        {
            (int me, string meSubject) = await fixture.SeedCallerAsync("Me nine");
            (_, string otherSubject) = await fixture.SeedCallerAsync("Other nine");
            (_, string adminSubject) = await fixture.SeedCallerAsync("Admin nine");
            string publisher = await PublisherAsync(adminSubject, AccessActions.Share);
            int mine = await CreateAsync(meSubject);
            int theirs = await CreateAsync(otherSubject);
            int unshared = await CreateAsync(otherSubject);
            int standard = await SeedStandardAsync("Standard nine");
            await ShareAsync(otherSubject, [theirs], new ShareArguments([new UserGrant(me, ShareAccess.View)], null));
            await ShareAsync(publisher, [standard], new ShareArguments([new UserGrant(me, ShareAccess.View)], null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(meSubject);
            RowPage page = await Reports(scope).QueryAsync(
                new QueryRequest { Select = "Id", NamedFilters = [ShareableNames.SharedWithMeFilter], Filters = [Ids(mine, theirs, unshared, standard)], OrderBy = "Id" },
                Token);

            Assert.Equal([theirs, standard], Column(page.Rows));
        }

        [Fact]
        public async Task The_shares_extra_lists_the_grants_of_the_rows_the_caller_may_share_with_their_grantees_as_related_entities()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner ten");
            (int bea, string beaSubject) = await fixture.SeedCallerAsync("Bea ten");
            (int ada, _) = await fixture.SeedCallerAsync("Ada ten");
            int role = await fixture.SeedRoleAsync();
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);
            await PublishAsync(publisher, [id], new PublishArguments([new RoleGrant(role, ShareAccess.Edit)], null, Everyone: true));
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(bea, ShareAccess.Edit), new UserGrant(ada, ShareAccess.View)], null));

            // --- The owner: users, then the role, then everyone, with the users and the role related ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(publisher))
            {
                EntitiesResult<Report> read = await Reports(scope).GetByIdAsync(id, new DetailsRequest { Include = [ShareableNames.SharesExtra] }, Token);
                JsonElement entry = Assert.Single(read.Extras![ShareableNames.SharesExtra].EnumerateArray());
                Assert.Equal(id, entry.GetProperty("id").GetInt32());
                JsonElement[] grants = [.. entry.GetProperty("grants").EnumerateArray()];
                Assert.Equal(
                    [(bea, null, "Edit"), (ada, null, "View"), (null, role, "Edit"), (null, null, "View")],
                    grants.Select(static grant => (Int(grant, "userId"), Int(grant, "roleId"), grant.GetProperty("access").GetString())));
                Assert.All(grants, grant => Assert.Equal(owner, grant.GetProperty("sharedById").GetInt32()));
                Assert.NotNull(read.Related);
                Assert.Superset(new HashSet<int> { bea, ada }, RelatedIds<User>(read, "core.User").ToHashSet());
                Assert.Contains(role, RelatedIds<Role>(read, "core.Role"));
            }

            // --- An Edit grantee may not share: the extra answers nothing for the row, no 403, and no grantee is related ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(beaSubject))
            {
                EntitiesResult<Report> read = await Reports(scope).GetByIdAsync(id, new DetailsRequest { Include = [ShareableNames.SharesExtra] }, Token);
                Assert.Equal(0, read.Extras![ShareableNames.SharesExtra].GetArrayLength());
                Assert.DoesNotContain(ada, RelatedIds<User>(read, "core.User"));
                Assert.Empty(RelatedIds<Role>(read, "core.Role"));
            }
        }

        // --- The share notice ---

        [Fact]
        public async Task A_new_user_grant_notifies_its_grantee_once_a_removed_one_withdraws_it_and_role_and_everyone_grants_notify_no_one()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner eleven");
            (int grantee, _) = await fixture.SeedCallerAsync("Grantee eleven");
            (int member, _) = await fixture.SeedCallerAsync("Member eleven");
            int role = await fixture.SeedRoleAsync();
            await fixture.SeedMembershipAsync(member, role);
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);
            string notices = $"SELECT COUNT(*) FROM [core].[Notifications] WHERE [Type] = N'{Notice}' AND [DedupKey] = N'core.Report:{id}'";

            // --- A new grant notifies; changing its access notifies no one again ---
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null));
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.Edit)], null));
            Assert.Equal(1, await ScalarAsync<int>(notices));
            Assert.Equal(
                (grantee, owner, Resource, id),
                (await ScalarAsync<int>($"SELECT [UserId] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'"),
                 await ScalarAsync<int>($"SELECT [ActorUserId] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'"),
                 await ScalarAsync<string>($"SELECT [TargetResource] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'"),
                 await ScalarAsync<long>($"SELECT [TargetId] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'")));

            // --- In a tenant of one content language, each text fills its first slot alone ---
            string json = await ScalarAsync<string>($"SELECT [ArgumentsJson] FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'") ?? "null";
            using (var arguments = JsonDocument.Parse(json))
            {
                Assert.Equal(["Sales", null, null], Slots(arguments.RootElement.GetProperty("title")));
                Assert.Equal(["Owner eleven", null, null], Slots(arguments.RootElement.GetProperty("actorName")));
            }

            // --- Role and Everyone grants notify no one ---
            await PublishAsync(publisher, [id], new PublishArguments([new RoleGrant(role, ShareAccess.View)], null, Everyone: true));
            Assert.Equal(1, await ScalarAsync<int>(notices));

            // --- Removing the grant withdraws the notice; granting again raises one, not two ---
            await ShareAsync(publisher, [id], new ShareArguments(null, [grantee]));
            Assert.Equal(0, await ScalarAsync<int>(notices));
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null));
            await ShareAsync(publisher, [id], new ShareArguments(null, [grantee]));
            await ShareAsync(publisher, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null));
            Assert.Equal(1, await ScalarAsync<int>(notices));
        }

        [Fact]
        public async Task The_share_notice_carries_the_rows_title_and_the_sharers_name_in_every_content_language()
        {
            // --- The second tenant, which no other case seeds: English content first, then Arabic, on an instance that offers both ---
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantB,
                $"UPDATE [core].[Settings] SET [SecondaryLanguage] = 'ar'; UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Settings}';");
            await using ServiceProvider bilingual = fixture.CreateInstance(static services => services.Replace(ServiceDescriptor.Singleton(new LanguageDeclaration(["en", "ar"], []))));
            (int owner, string ownerSubject) = await SeedSecondTenantUserAsync("Owner twelve", "مالك");
            (int grantee, _) = await SeedSecondTenantUserAsync("Grantee twelve", "مستلم");

            // --- A report titled in both languages, shared with the grantee ---
            int id;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(ownerSubject, tenantId: ConnectDatabase.TenantB, services: bilingual))
            {
                Report report = new()
                {
                    Title = "Sales",
                    Title2 = "مبيعات",
                    Root = "core.User",
                    Kind = ReportKind.List,
                    DefinitionJson = Definition,
                };
                EntitiesResult<Report> saved = await Reports(scope).SaveAsync([report], new SaveOptions { ReturnEntities = false }, Token);
                id = checked((int)Assert.Single(saved.Ids));
            }

            Assert.Equal("مبيعات", await fixture.ScalarAsync<string>(ConnectDatabase.TenantB, $"SELECT [Title2] FROM [core].[Reports] WHERE [Id] = {id}"));
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(ownerSubject, tenantId: ConnectDatabase.TenantB, services: bilingual))
            {
                _ = await Reports(scope).ExecuteActionAsync(ShareableNames.ShareAction, [id], new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null), new ActionOptions(), Token);
            }

            // --- The notice's arguments: each text in its three content slots ---
            string where = $"FROM [core].[Notifications] WHERE [DedupKey] = N'core.Report:{id}'";
            Assert.Equal((grantee, owner), (await fixture.ScalarAsync<int>(ConnectDatabase.TenantB, $"SELECT [UserId] {where}"), await fixture.ScalarAsync<int>(ConnectDatabase.TenantB, $"SELECT [ActorUserId] {where}")));
            string json = await fixture.ScalarAsync<string>(ConnectDatabase.TenantB, $"SELECT [ArgumentsJson] {where}") ?? "null";
            using var arguments = JsonDocument.Parse(json);
            Assert.Equal(["Sales", "مبيعات", null], Slots(arguments.RootElement.GetProperty("title")));
            Assert.Equal(["Owner twelve", "مالك", null], Slots(arguments.RootElement.GetProperty("actorName")));
        }

        // --- The grant caps and the grantees' shape ---

        [Fact]
        public async Task The_user_and_role_caps_admit_a_row_at_each_and_refuse_one_grant_past_it()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner thirteen");
            int[] users = await SeedUsersAsync(ShareableNames.MaxUserGrants + 1, "Grantee thirteen");
            int[] roles = await SeedRolesAsync(ShareableNames.MaxRoleGrants + 1);
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);
            string userGrants = $"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id} AND [UserId] IS NOT NULL";
            string roleGrants = $"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id} AND [RoleId] IS NOT NULL";

            // --- One user past the cap in one call: refused, nothing written ---
            ValidationException tooMany = await Assert.ThrowsAsync<ValidationException>(() => ShareAsync(publisher, [id], new ShareArguments([.. users.Select(static user => new UserGrant(user, ShareAccess.View))], null)));
            ValidationError error = Assert.Single(tooMany.Errors);
            Assert.Equal(("Arguments.Set", SharingValidationCodes.TooManyGrants, "user", ShareableNames.MaxUserGrants), (error.Path.ToString(), error.Code, error.Arguments["kind"], error.Arguments["max"]));
            Assert.Equal(0, await ScalarAsync<int>(userGrants));

            // --- At the cap: admitted; one more on top of the stored grants: refused ---
            await ShareAsync(publisher, [id], new ShareArguments([.. users.SkipLast(1).Select(static user => new UserGrant(user, ShareAccess.View))], null));
            Assert.Equal(ShareableNames.MaxUserGrants, await ScalarAsync<int>(userGrants));
            ValidationException onTop = await Assert.ThrowsAsync<ValidationException>(() => ShareAsync(publisher, [id], new ShareArguments([new UserGrant(users[^1], ShareAccess.View)], null)));
            Assert.Equal(("Arguments.Set", SharingValidationCodes.TooManyGrants), Only(onTop));

            // --- The same for roles, at their own cap ---
            ValidationException tooManyRoles = await Assert.ThrowsAsync<ValidationException>(() => PublishAsync(publisher, [id], new PublishArguments([.. roles.Select(static role => new RoleGrant(role, ShareAccess.View))], null, null)));
            ValidationError roleError = Assert.Single(tooManyRoles.Errors);
            Assert.Equal(("Arguments.Set", SharingValidationCodes.TooManyGrants, "role", ShareableNames.MaxRoleGrants), (roleError.Path.ToString(), roleError.Code, roleError.Arguments["kind"], roleError.Arguments["max"]));
            Assert.Equal(0, await ScalarAsync<int>(roleGrants));
            await PublishAsync(publisher, [id], new PublishArguments([.. roles.SkipLast(1).Select(static role => new RoleGrant(role, ShareAccess.View))], null, null));
            Assert.Equal(ShareableNames.MaxRoleGrants, await ScalarAsync<int>(roleGrants));
        }

        [Fact]
        public async Task Two_concurrent_shares_that_each_fit_the_user_cap_alone_commit_one_and_the_table_refuses_the_other()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner fourteen");
            int[] users = await SeedUsersAsync(ShareableNames.MaxUserGrants + 1, "Grantee fourteen");
            int id = await CreateAsync(ownerSubject);

            // --- One grant short of the cap, the two left out apart in the grantees' order ---
            int first = users[50];
            int second = users[150];
            await ShareAsync(ownerSubject, [id], new ShareArguments([.. users.Where(user => user != first && user != second).Select(static user => new UserGrant(user, ShareAccess.View))], null));
            Assert.Equal(ShareableNames.MaxUserGrants - 1, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));

            // --- Each adds one grantee while the share table is held, so both pass validation and write together ---
            await using AsyncServiceScope one = await fixture.ScopeAsync(ownerSubject);
            await using AsyncServiceScope other = await fixture.ScopeAsync(ownerSubject);
            IReadOnlyList<Task> shares = await fixture.HoldWhileAsync(
                "SELECT COUNT(*) FROM [core].[ReportShares] WITH (TABLOCKX, HOLDLOCK);",
                2,
                "OBJECT",
                () => [ShareInAsync(one, id, first), ShareInAsync(other, id, second)]);
            Exception?[] outcomes = [await CaptureAsync(shares[0]), await CaptureAsync(shares[1])];

            // --- One commits; the other, run after it, finds the cap reached ---
            ValidationException refused = Assert.IsType<ValidationException>(Assert.Single(outcomes, static outcome => outcome is not null));
            Assert.Equal([SharingValidationCodes.TooManyGrants], refused.Codes());
            Assert.Equal(ShareableNames.MaxUserGrants, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));
        }

        [Fact]
        public async Task The_grants_set_refuse_a_hole_a_grantee_twice_and_a_grantee_both_set_and_removed()
        {
            (_, string ownerSubject) = await fixture.SeedCallerAsync("Owner fifteen");
            (int ann, _) = await fixture.SeedCallerAsync("Ann fifteen");
            (int ben, _) = await fixture.SeedCallerAsync("Ben fifteen");
            int role = await fixture.SeedRoleAsync();
            string publisher = await PublisherAsync(ownerSubject);
            int id = await CreateAsync(publisher);

            // --- A hole, a user set twice, a user both set and removed, and one removed twice ---
            ValidationException share = await Assert.ThrowsAsync<ValidationException>(() => ShareAsync(
                publisher,
                [id],
                new ShareArguments([new UserGrant(ann, ShareAccess.View), null!, new UserGrant(ann, ShareAccess.Edit)], [ann, ben, ben])));
            Assert.Equal(
                [("Arguments.Set[1]", ValidationCodes.Required), ("Arguments.Set[2].UserId", ValidationCodes.Unique), ("Arguments.Remove[0]", ValidationCodes.Unique), ("Arguments.Remove[2]", ValidationCodes.Unique)],
                share.Errors.Select(static error => (error.Path.ToString(), error.Code)));

            // --- The same rules for roles ---
            ValidationException publish = await Assert.ThrowsAsync<ValidationException>(() => PublishAsync(
                publisher,
                [id],
                new PublishArguments([null!, new RoleGrant(role, ShareAccess.View), new RoleGrant(role, ShareAccess.View)], [role], null)));
            Assert.Equal(
                [("Arguments.Set[0]", ValidationCodes.Required), ("Arguments.Set[2].RoleId", ValidationCodes.Unique), ("Arguments.Remove[0]", ValidationCodes.Unique)],
                publish.Errors.Select(static error => (error.Path.ToString(), error.Code)));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id}"));
        }

        // --- Helpers ---

        /// <summary>The reports' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Report, int> Reports(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Report, int>>();
        }

        /// <summary>Creates a report as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="title">The title; empty for the default one.</param>
        /// <returns>The report's id.</returns>
        private async Task<int> CreateAsync(string subject, string title = "Sales")
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            Report report = new()
            {
                Title = title,
                Root = "core.User",
                Kind = ReportKind.List,
                DefinitionJson = Definition,
            };
            EntitiesResult<Report> saved = await Reports(scope).SaveAsync([report], new SaveOptions { ReturnEntities = false }, Token);
            return checked((int)Assert.Single(saved.Ids));
        }

        /// <summary>Renames a report as a caller, sending back the row as it reads it.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="id">The report.</param>
        /// <param name="title">The new title.</param>
        /// <returns>A task that completes when the save does.</returns>
        private async Task RenameAsync(string subject, int id, string title)
        {
            // --- The stamp the row holds, which an update must echo ---
            DateTimeOffset stamp = await fixture.ScalarAsync<DateTimeOffset>(ConnectDatabase.TenantA, $"SELECT [ModifiedAt] FROM [core].[Reports] WHERE [Id] = {id}");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            Report report = new()
            {
                Id = id,
                Title = title,
                Root = "core.User",
                Kind = ReportKind.List,
                DefinitionJson = Definition,
                ModifiedAt = stamp,
            };
            _ = await Reports(scope).SaveAsync([report], new SaveOptions { ReturnEntities = false }, Token);
        }

        /// <summary>Seeds a standard report, owned by no one.</summary>
        /// <param name="title">The title.</param>
        /// <returns>The report's id.</returns>
        private Task<int> SeedStandardAsync(string title)
        {
            int system = WellKnownIds.SystemUserId;
            int version = QueryexLanguage.Version;
            return ScalarAsync<int>(
                $$"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Reports];
                INSERT INTO [core].[Reports] ([Id], [Title], [Root], [Kind], [DefinitionJson], [QueryexVersion], [OwnerId], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (@id, N'{{title}}', 'core.User', 'List', N'{{Definition}}', {{version}}, NULL, 1, SYSDATETIMEOFFSET(), {{system}}, SYSDATETIMEOFFSET(), {{system}});
                SELECT @id;
                """);
        }

        /// <summary>An insert of an Everyone grant of a report, bypassing the actions.</summary>
        /// <param name="id">The report.</param>
        /// <param name="access">The grant's access.</param>
        /// <returns>The statement.</returns>
        private static string EveryoneGrant(int id, ShareAccess access)
        {
            int system = WellKnownIds.SystemUserId;
            return $"INSERT INTO [core].[ReportShares] ([Id], [ReportId], [UserId], [RoleId], [Access], [SharedById], [SharedAt]) VALUES (NEXT VALUE FOR [core].[sq_ReportShares], {id}, NULL, NULL, '{access}', {system}, SYSDATETIMEOFFSET());";
        }

        /// <summary>Seeds active, joined individuals of the first tenant in one round trip, under fresh ids.</summary>
        /// <param name="count">How many.</param>
        /// <param name="name">The name each carries, followed by its id.</param>
        /// <returns>The ids, ascending.</returns>
        private async Task<int[]> SeedUsersAsync(int count, string name)
        {
            int[] ids = [.. Enumerable.Range(0, count).Select(_ => fixture.NextUserId())];
            int system = WellKnownIds.SystemUserId;
            string users = string.Join(
                ",\n",
                ids.Select(id => string.Create(CultureInfo.InvariantCulture, $"({id}, 'Individual', 'sub-{id}', N'sub-{id}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{name} {id}', 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system})")));
            string stamps = string.Join(", ", ids.Select(static id => string.Create(CultureInfo.InvariantCulture, $"({id})")));
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES {users};
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES {stamps};
                """);
            return ids;
        }

        /// <summary>Seeds active, private roles of the first tenant without permissions.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The ids, in seeding order.</returns>
        private async Task<int[]> SeedRolesAsync(int count)
        {
            int[] ids = new int[count];
            for (int index = 0; index < count; index++)
            {
                ids[index] = await fixture.SeedRoleAsync();
            }

            return ids;
        }

        /// <summary>Seeds an active individual of the second tenant with a name in its second content language.</summary>
        /// <param name="name">The name.</param>
        /// <param name="name2">The name in the second content language.</param>
        /// <returns>The id and the subject.</returns>
        private async Task<(int UserId, string Subject)> SeedSecondTenantUserAsync(string name, string name2)
        {
            int userId = fixture.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await fixture.SeedUserAsync(ConnectDatabase.TenantB, userId, subject, name);
            await fixture.ExecuteAsync(ConnectDatabase.TenantB, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [Name2] = N'{name2}' WHERE [Id] = {userId};"));
            return (userId, subject);
        }

        /// <summary>Gives a caller a role that publishes reports, and any other actions named, over every report.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="actions">The other actions.</param>
        /// <returns>The caller's subject.</returns>
        private async Task<string> PublisherAsync(string subject, params string[] actions)
        {
            int user = int.Parse(subject["sub-".Length..], CultureInfo.InvariantCulture);
            int role = await fixture.SeedRoleAsync([.. actions.Prepend(AccessActions.Publish).Select(static action => (Resource, action, (string?)null))]);
            await fixture.SeedMembershipAsync(user, role);
            return subject;
        }

        /// <summary>Shares reports as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="ids">The reports.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the action does.</returns>
        private Task ShareAsync(string subject, IReadOnlyList<int> ids, ShareArguments arguments)
        {
            return ActAsync(subject, ShareableNames.ShareAction, ids, arguments);
        }

        /// <summary>Publishes reports as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="ids">The reports.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the action does.</returns>
        private Task PublishAsync(string subject, IReadOnlyList<int> ids, PublishArguments arguments)
        {
            return ActAsync(subject, ShareableNames.PublishAction, ids, arguments);
        }

        /// <summary>Transfers reports as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="ids">The reports.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the action does.</returns>
        private Task TransferAsync(string subject, IReadOnlyList<int> ids, TransferArguments arguments)
        {
            return ActAsync(subject, ShareableNames.TransferAction, ids, arguments);
        }

        /// <summary>Runs an action as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="action">The action.</param>
        /// <param name="ids">The targets.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the action does.</returns>
        private async Task ActAsync(string subject, string action, IReadOnlyList<int> ids, object arguments)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await Reports(scope).ExecuteActionAsync(action, ids, arguments, new ActionOptions(), Token);
        }

        /// <summary>Shares a report with one user at view access, in a scope opened beforehand.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="id">The report.</param>
        /// <param name="user">The grantee.</param>
        /// <returns>A task that completes when the action does.</returns>
        private static async Task ShareInAsync(AsyncServiceScope scope, int id, int user)
        {
            _ = await Reports(scope).ExecuteActionAsync(ShareableNames.ShareAction, [id], new ShareArguments([new UserGrant(user, ShareAccess.View)], null), new ActionOptions(), Token);
        }

        /// <summary>The refusal a task ends with, or null when it succeeds.</summary>
        /// <param name="task">The task.</param>
        /// <returns>The refusal.</returns>
        private static async Task<Exception?> CaptureAsync(Task task)
        {
            try
            {
                await task;
                return null;
            }
            catch (ValidationException exception)
            {
                return exception;
            }
        }

        /// <summary>The ids of the related instances of one entity a read carries; none when it carries no such set.</summary>
        /// <typeparam name="T">The entity's leaf.</typeparam>
        /// <param name="read">The read.</param>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The ids.</returns>
        private static IEnumerable<int> RelatedIds<T>(EntitiesResult<Report> read, string entity)
            where T : Entity<int>
        {
            return read.Related is not null && read.Related.Sets.TryGetValue(entity, out RelatedEntitySet? set) ? set.Entities.Cast<T>().Select(static instance => instance.Id) : [];
        }

        /// <summary>The three content slots of a multilingual text in JSON.</summary>
        /// <param name="text">The text's array.</param>
        /// <returns>The slots, null for an empty one.</returns>
        private static IEnumerable<string?> Slots(JsonElement text)
        {
            return text.EnumerateArray().Select(static slot => slot.ValueKind == JsonValueKind.Null ? null : slot.GetString());
        }

        /// <summary>The ids among some reports that a caller's query reads, inactive ones included.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="ids">The reports; none for every report.</param>
        /// <returns>The ids read, in order.</returns>
        private async Task<IReadOnlyList<int>> VisibleAsync(string subject, params int[] ids)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            RowPage page = await Reports(scope).QueryAsync(
                new QueryRequest { Select = "Id", Filters = ids.Length == 0 ? null : [Ids(ids)], OrderBy = "Id", IncludeInactive = true },
                Token);
            return Column(page.Rows);
        }

        /// <summary>A filter of the reports among some ids.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The filter.</returns>
        private static string Ids(params int[] ids)
        {
            return "Id in (" + string.Join(", ", ids.Select(static id => id.ToString(CultureInfo.InvariantCulture))) + ")";
        }

        /// <summary>The integers of a row set's first column.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The integers.</returns>
        private static List<int> Column(QueryRowSet rows)
        {
            int[] buffer = (int[])rows.GetBuffer(0);
            return [.. buffer.Take(rows.RowCount)];
        }

        /// <summary>A nullable integer member of a JSON object, which the serializer omits when null.</summary>
        /// <param name="element">The object.</param>
        /// <param name="name">The member.</param>
        /// <returns>The integer, or null.</returns>
        private static int? Int(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;
        }

        /// <summary>The one error of a refusal, as its path and code.</summary>
        /// <param name="exception">The refusal.</param>
        /// <returns>The path and code.</returns>
        private static (string Path, string Code) Only(ValidationException exception)
        {
            ValidationError error = Assert.Single(exception.Errors);
            return (error.Path.ToString(), error.Code);
        }

        /// <summary>A scalar from tenant A.</summary>
        /// <typeparam name="T">The scalar's type.</typeparam>
        /// <param name="query">The query.</param>
        /// <returns>The scalar.</returns>
        private Task<T?> ScalarAsync<T>(string query)
        {
            return fixture.ScalarAsync<T>(ConnectDatabase.TenantA, query);
        }

        /// <summary>The texts of the commands that read the reports, sent while an operation runs in this flow.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>The texts.</returns>
        private static async Task<IReadOnlyList<string>> CapturedAsync(Func<Task> operation)
        {
            using CommandCapture capture = new();
            capture.Start();
            await operation();
            return [.. capture.Texts.Where(static text => text.Contains("[core].[Reports]", StringComparison.Ordinal))];
        }

        /// <summary>
        ///     Captures the text of each command the SQL client sends from the flow that started the
        ///     capture, through the client's diagnostic events.
        /// </summary>
        private sealed class CommandCapture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
        {
            /// <summary>The SQL client's diagnostic listener's name.</summary>
            private const string ListenerName = "SqlClientDiagnosticListener";

            /// <summary>The event raised before a command is sent.</summary>
            private const string BeforeCommand = "Microsoft.Data.SqlClient.WriteCommandBefore";

            /// <summary>Marks the flows whose commands are captured.</summary>
            private static readonly AsyncLocal<CommandCapture?> Current = new();

            /// <summary>The subscriptions, released on disposal.</summary>
            private readonly List<IDisposable> _subscriptions = [];

            /// <summary>The texts captured.</summary>
            private readonly List<string> _texts = [];

            /// <summary>The texts captured, in order.</summary>
            public IReadOnlyList<string> Texts
            {
                get
                {
                    lock (_texts)
                    {
                        return [.. _texts];
                    }
                }
            }

            /// <summary>Starts capturing the commands of the current flow.</summary>
            public void Start()
            {
                Current.Value = this;
                _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
            }

            /// <inheritdoc />
            public void OnNext(DiagnosticListener value)
            {
                if (value.Name == ListenerName)
                {
                    lock (_subscriptions)
                    {
                        _subscriptions.Add(value.Subscribe(this));
                    }
                }
            }

            /// <inheritdoc />
            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Key == BeforeCommand
                    && ReferenceEquals(Current.Value, this)
                    && value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand command)
                {
                    lock (_texts)
                    {
                        _texts.Add(command.CommandText);
                    }
                }
            }

            /// <inheritdoc />
            public void OnCompleted()
            {
            }

            /// <inheritdoc />
            public void OnError(Exception error)
            {
            }

            /// <inheritdoc />
            public void Dispose()
            {
                Current.Value = null;
                lock (_subscriptions)
                {
                    foreach (IDisposable subscription in _subscriptions)
                    {
                        subscription.Dispose();
                    }

                    _subscriptions.Clear();
                }
            }
        }
    }
}
