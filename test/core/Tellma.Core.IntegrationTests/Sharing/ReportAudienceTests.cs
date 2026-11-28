// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.IntegrationTests.Access;

namespace Tellma.Core.IntegrationTests.Sharing
{
    /// <summary>
    ///     The audience of a stored report against a real tenant database: who cannot read the data,
    ///     per grant and per member; a bespoke criterion that lets every user read it; a service
    ///     account through a shared role, a public role and a role's stored permission; a deactivated
    ///     grantee; the members hidden from a caller who cannot read the roles; the cap with an exact
    ///     total; a role's stored permission with more holders than a page counted exactly; a filtered
    ///     permission counted only for the rows that satisfy it; a row seen through; and the
    ///     instrument. The class has its own databases, so it alone seeds their users, roles and reports.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class ReportAudienceTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The reports' resource.</summary>
        private const string Resource = "core.Report";

        /// <summary>A list of the users' names, the document every report here holds.</summary>
        private const string Definition = /*lang=json,strict*/ """{ "formatVersion": 1, "list": { "columns": [ { "path": "Name" } ] } }""";

        /// <summary>The data securable the resolver's own tests ask about: the roles, which no bespoke criterion reads.</summary>
        private static readonly SecurableRef RoleRead = new(CoreResources.Role, AccessActions.Read);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Each_grant_and_each_member_count_who_cannot_read_the_data()
        {
            // --- Owned by a reader of the roles; shared with a reader, a non-reader and a role of one of each ---
            (int owner, string ownerSubject) = await OwnerAsync("Owner one");
            (int reader, _) = await fixture.SeedCallerAsync("Reader one");
            (int stranger, _) = await fixture.SeedCallerAsync("Stranger one");
            (int memberReader, _) = await fixture.SeedCallerAsync("Member reader one");
            (int memberStranger, _) = await fixture.SeedCallerAsync("Member stranger one");
            await fixture.SeedMembershipAsync(reader, await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null)));
            await fixture.SeedMembershipAsync(memberReader, await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Save, null)));
            int role = await fixture.SeedRoleAsync();
            await fixture.SeedMembershipAsync(memberReader, role);
            await fixture.SeedMembershipAsync(memberStranger, role);
            int id = await CreateAsync(ownerSubject);
            await ShareAsync(ownerSubject, id, new ShareArguments([new UserGrant(reader, ShareAccess.View), new UserGrant(stranger, ShareAccess.Edit)], null));
            await GrantAsync(id, role, ShareAccess.View);

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead]);

            // --- Per grant: its members and who among them cannot read the roles ---
            Assert.Equal(
                [
                    (AudienceReasonKind.Owner, owner, null, AudienceAccess.Owner, 1, 0),
                    (AudienceReasonKind.UserShare, reader, null, AudienceAccess.View, 1, 0),
                    (AudienceReasonKind.UserShare, stranger, null, AudienceAccess.Edit, 1, 1),
                    (AudienceReasonKind.RoleShare, null, role, AudienceAccess.View, 2, 1),
                ],
                audience.Grants.Where(static grant => grant.Kind != AudienceReasonKind.Permission)
                    .Select(static grant => (grant.Kind, grant.UserId, grant.RoleId, grant.Access, grant.Members, grant.CannotRead[0])));

            // --- Per member: its access, and the data it lacks; the total exact ---
            Assert.Equal((5, false, 2), (audience.Total, audience.Capped, audience.CannotRead[0]));
            Assert.Equal(
                [(owner, AudienceAccess.Owner, 0), (reader, AudienceAccess.View, 0), (stranger, AudienceAccess.Edit, 1), (memberReader, AudienceAccess.View, 0), (memberStranger, AudienceAccess.View, 1)],
                audience.Members!.Select(static member => (member.UserId, member.Access, member.CannotRead.Count)).OrderBy(member => member.UserId == owner ? 0 : 1).ThenBy(static member => member.UserId));
            Assert.Contains(stranger, RelatedIds(audience, "core.User"));
            Assert.Contains(role, RelatedIds(audience, "core.Role"));
        }

        [Fact]
        public async Task The_report_route_reads_its_root_and_a_bespoke_criterion_on_it_lets_every_member_read_it()
        {
            // --- The users may read their own row, so every member reads the root core.User ---
            (_, string ownerSubject) = await OwnerAsync("Owner two");
            (int stranger, _) = await fixture.SeedCallerAsync("Stranger two");
            int id = await CreateAsync(ownerSubject);
            await ShareAsync(ownerSubject, id, new ShareArguments([new UserGrant(stranger, ShareAccess.View)], null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(ownerSubject);
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Find(Resource)!.Actions
                .OfType<ApiActionDescriptor>().Single(static candidate => candidate.Name == "audience");
            ShareAudience audience = Assert.IsType<ShareAudience>(await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, new IdRequest(id), Token));

            Assert.Equal([0], audience.CannotRead);
            Assert.All(audience.Grants, static grant => Assert.Equal([0], grant.CannotRead));
            Assert.All(audience.Members!, static member => Assert.Empty(member.CannotRead));

            // --- The users' projection carries their kind ---
            Assert.Contains("Kind", audience.Related.Sets["core.User"].Projection);
        }

        [Fact]
        public async Task A_service_account_counts_through_a_shared_role_a_public_role_and_a_roles_stored_permission()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner three");
            int service = fixture.NextUserId();
            await fixture.SeedUserAsync(ConnectDatabase.TenantA, service, string.Create(CultureInfo.InvariantCulture, $"sub-{service}"), "Service three", UserKind.Service);

            // --- A shared role: the service account is a member ---
            int shared = await fixture.SeedRoleAsync();
            await fixture.SeedMembershipAsync(service, shared);
            int first = await CreateAsync(ownerSubject);
            await GrantAsync(first, shared, ShareAccess.Edit);
            AudienceMember viaRole = Assert.Single((await ResolveAsync(ownerSubject, first, [])).Members!, member => member.UserId == service);
            Assert.Equal((AudienceAccess.Edit, AudienceReasonKind.RoleShare, shared), (viaRole.Access, Assert.Single(viaRole.Reasons).Kind, viaRole.Reasons[0].RoleId));

            // --- A public role: every audience user, the service account among them ---
            int open = await fixture.SeedRoleAsync();
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {open};");
            int second = await CreateAsync(ownerSubject);
            await GrantAsync(second, open, ShareAccess.View);
            ShareAudience everyone = await ResolveAsync(ownerSubject, second, []);
            int audienceUsers = await AudienceUsersAsync();
            Assert.Equal(audienceUsers, everyone.Total);
            Assert.Equal(audienceUsers, Assert.Single(everyone.Grants, static grant => grant.Kind == AudienceReasonKind.RoleShare).Members);
            Assert.Contains(everyone.Members!, member => member.UserId == service);
            Assert.DoesNotContain(everyone.Members!, static member => member.UserId == WellKnownIds.SystemUserId);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsPublic] = 0 WHERE [Id] = {open};");

            // --- A role's stored permission to read every report ---
            int third = await CreateAsync(ownerSubject);
            int readers = await fixture.SeedRoleAsync((Resource, AccessActions.Read, Only(third)));
            await fixture.SeedMembershipAsync(service, readers);
            ShareAudience permitted = await ResolveAsync(ownerSubject, third, []);
            AudienceReason reason = Assert.Single(Assert.Single(permitted.Members!, member => member.UserId == service).Reasons, static reason => reason.RoleId is not null && reason.Kind == AudienceReasonKind.Permission);
            Assert.Equal((readers, true, AudienceAccess.View), (reason.RoleId, reason.Filtered, reason.Access));
            Assert.Contains(permitted.Grants, grant => grant.Kind == AudienceReasonKind.Permission && grant.RoleId == readers && grant.PermissionId == reason.PermissionId);
        }

        [Fact]
        public async Task A_deactivated_grantee_stays_in_the_grants_with_no_members()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner four");
            (int grantee, _) = await fixture.SeedCallerAsync("Grantee four");
            int id = await CreateAsync(ownerSubject);
            await ShareAsync(ownerSubject, id, new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {grantee};");

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead]);

            AudienceGrant grant = Assert.Single(audience.Grants, static grant => grant.Kind == AudienceReasonKind.UserShare);
            Assert.Equal((grantee, 0, 0), (grant.UserId, grant.Members, grant.CannotRead[0]));
            Assert.DoesNotContain(audience.Members!, member => member.UserId == grantee);
            Assert.Contains(grantee, RelatedIds(audience, "core.User"));
        }

        [Fact]
        public async Task A_caller_who_cannot_read_the_roles_sees_grants_and_counts_but_no_member_and_a_stranger_finds_no_report()
        {
            (int owner, string ownerSubject) = await fixture.SeedCallerAsync("Owner five");
            (int grantee, string strangerSubject) = await fixture.SeedCallerAsync("Grantee five");
            int id = await CreateAsync(ownerSubject);
            await ShareAsync(ownerSubject, id, new ShareArguments([new UserGrant(grantee, ShareAccess.View)], null));

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead]);

            Assert.Null(audience.Members);
            Assert.False(audience.Capped);
            Assert.Equal((2, 2), (audience.Total, audience.CannotRead[0]));
            Assert.Equal([owner, grantee], audience.Grants.Where(static grant => grant.UserId is not null).Select(static grant => grant.UserId!.Value));

            // --- A grantee to view may not share, so the report is not found for it ---
            _ = await Assert.ThrowsAsync<NotFoundException>(() => ResolveAsync(strangerSubject, id, [RoleRead]));
        }

        [Fact]
        public async Task The_members_are_cut_at_the_cap_while_the_total_stays_exact()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner six");
            int[] users = await SeedUsersAsync(6, "Capped six");
            int role = await RoleOfAsync(users);
            int id = await CreateAsync(ownerSubject);
            await GrantAsync(id, role, ShareAccess.View);
            await using ServiceProvider capped = fixture.CreateInstance(static services => services.Configure<SharingOptions>(static options => options.MaxAudienceMembers = 3));

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead], capped);

            Assert.Equal((7, true, 3), (audience.Total, audience.Capped, audience.Members!.Count));
            Assert.Equal(6, Assert.Single(audience.Grants, static grant => grant.Kind == AudienceReasonKind.RoleShare).CannotRead[0]);
            Assert.Equal(6, audience.CannotRead[0]);
        }

        [Fact]
        public async Task A_roles_stored_permission_with_more_holders_than_a_page_is_counted_exactly()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner seven");
            int id = await CreateAsync(ownerSubject);
            int[] users = await SeedUsersAsync(600, "Holder seven");
            int role = await RoleOfAsync(users, (Resource, AccessActions.Read, Only(id)));
            using MetricCollector<long> members = new(fixture.Services.GetRequiredService<IMeterFactory>(), SharingTelemetryNames.MeterName, SharingTelemetryNames.AudienceMembers);

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead]);

            AudienceGrant grant = Assert.Single(audience.Grants, grant => grant.RoleId == role);
            Assert.Equal((AudienceReasonKind.Permission, 600, 600), (grant.Kind, grant.Members, grant.CannotRead[0]));
            Assert.Equal((601, 601), (audience.Total, audience.Members!.Count));

            // --- The instrument records the audience's size under its resource ---
            CollectedMeasurement<long> measured = Assert.Single(members.GetMeasurementSnapshot());
            Assert.Equal((601L, (object?)Resource), (measured.Value, measured.Tags[SharingTelemetryNames.ResourceTag]));
        }

        [Fact]
        public async Task A_filtered_permission_counts_only_for_the_reports_that_satisfy_it()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner eight");
            (int holder, _) = await fixture.SeedCallerAsync("Holder eight");
            int role = await fixture.SeedRoleAsync((Resource, AccessActions.Save, "Title = 'Alpha eight'"));
            await fixture.SeedMembershipAsync(holder, role);
            int alpha = await CreateAsync(ownerSubject, "Alpha eight");
            int beta = await CreateAsync(ownerSubject, "Beta eight");

            ShareAudience satisfied = await ResolveAsync(ownerSubject, alpha, []);
            ShareAudience unsatisfied = await ResolveAsync(ownerSubject, beta, []);

            AudienceReason reason = Assert.Single(Assert.Single(satisfied.Members!, member => member.UserId == holder).Reasons);
            Assert.Equal((AudienceReasonKind.Permission, role, true, AudienceAccess.Edit), (reason.Kind, reason.RoleId, reason.Filtered, reason.Access));
            Assert.Contains(satisfied.Grants, grant => grant.RoleId == role);
            Assert.DoesNotContain(unsatisfied.Members!, member => member.UserId == holder);
            Assert.DoesNotContain(unsatisfied.Grants, grant => grant.RoleId == role);
        }

        [Fact]
        public async Task A_row_seen_through_adds_its_audience_with_the_through_reason()
        {
            (_, string ownerSubject) = await OwnerAsync("Owner nine");
            (int viewer, _) = await fixture.SeedCallerAsync("Viewer nine");
            int id = await CreateAsync(ownerSubject);
            int host = await CreateAsync(ownerSubject, "Host nine");
            await ShareAsync(ownerSubject, host, new ShareArguments([new UserGrant(viewer, ShareAccess.Edit)], null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(ownerSubject);
            ShareAudience audience = await scope.ServiceProvider.GetRequiredService<IAudienceResolver>()
                .ResolveAsync(new AudienceQuery(Resource, id, [RoleRead], Resource, [host, host, id]), Token);

            AudienceGrant through = Assert.Single(audience.Grants, static grant => grant.Kind == AudienceReasonKind.Through);
            Assert.Equal((host, AudienceAccess.View, 2), (through.ThroughId, through.Access, through.Members));
            AudienceReason reason = Assert.Single(Assert.Single(audience.Members!, member => member.UserId == viewer).Reasons);
            Assert.Equal((AudienceReasonKind.Through, host, AudienceAccess.View), (reason.Kind, reason.ThroughId, reason.Access));
            Assert.Equal([host], RelatedIds(audience, Resource));
        }

        [Fact]
        public async Task A_permission_filtered_by_the_current_user_reaches_no_holder_whether_the_owner_or_an_administrator_reads_the_audience()
        {
            // --- The report authors may publish their own reports; the owner and another author hold it ---
            (int owner, string ownerSubject) = await OwnerAsync("Owner ten");
            (int author, _) = await fixture.SeedCallerAsync("Author ten");
            int authors = await fixture.SeedRoleAsync((Resource, AccessActions.Publish, "OwnerId = me()"));
            await fixture.SeedMembershipAsync(owner, authors);
            await fixture.SeedMembershipAsync(author, authors);
            int id = await CreateAsync(ownerSubject);

            // --- An administrator of this report, who reads the roles ---
            (int administrator, string administratorSubject) = await fixture.SeedCallerAsync("Administrator ten");
            await fixture.SeedMembershipAsync(administrator, await fixture.SeedRoleAsync((Resource, AccessActions.Share, Only(id)), (CoreResources.Role, AccessActions.Read, null)));

            // --- Either reader sees the authors' grant as depending on the viewer, with no member, and the same audience ---
            foreach (string subject in new[] { ownerSubject, administratorSubject })
            {
                ShareAudience audience = await ResolveAsync(subject, id, [RoleRead]);

                AudienceGrant grant = Assert.Single(audience.Grants, grant => grant.RoleId == authors);
                Assert.Equal((AudienceReasonKind.Permission, true, 0, 0), (grant.Kind, grant.DependsOnViewer, grant.Members, grant.CannotRead[0]));
                Assert.Equal([owner, administrator], audience.Members!.Select(static member => member.UserId).Order());
                Assert.Equal(2, audience.Total);
                Assert.DoesNotContain(audience.Members!.SelectMany(static member => member.Reasons), reason => reason.RoleId == authors);
                Assert.All(audience.Grants.Where(grant => grant.RoleId != authors), static grant => Assert.False(grant.DependsOnViewer));
            }
        }

        [Fact]
        public async Task An_everyone_grant_counts_the_readers_of_current_memberships_alone_and_a_public_reader_role_leaves_no_one_lacking()
        {
            // --- Readers of the roles through one role: a current, an expired and a future membership ---
            (_, string ownerSubject) = await OwnerAsync("Owner eleven");
            (int current, _) = await fixture.SeedCallerAsync("Current eleven");
            (int expired, _) = await fixture.SeedCallerAsync("Expired eleven");
            (int future, _) = await fixture.SeedCallerAsync("Future eleven");
            int readers = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(current, readers, startsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())", endsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(expired, readers, endsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(future, readers, startsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");

            // --- Shared with everyone, and with a role that is inactive ---
            int inactive = await fixture.SeedRoleAsync();
            await fixture.SeedMembershipAsync(current, inactive);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsActive] = 0 WHERE [Id] = {inactive};");
            int id = await CreateAsync(ownerSubject);
            await GrantAsync(id, null, ShareAccess.View);
            await GrantAsync(id, inactive, ShareAccess.View);

            ShareAudience audience = await ResolveAsync(ownerSubject, id, [RoleRead]);

            // --- Every audience user listed; the count of those lacking the roles agrees with the members' marks ---
            int audienceUsers = await AudienceUsersAsync();
            AudienceGrant everyone = Assert.Single(audience.Grants, static grant => grant.Kind == AudienceReasonKind.Everyone);
            Assert.Equal((audienceUsers, audienceUsers, false), (everyone.Members, audience.Total, audience.Capped));
            Assert.Equal(audienceUsers, audience.Members!.Count);
            int lacking = audience.Members!.Count(static member => member.CannotRead.Count > 0);
            Assert.Equal((lacking, lacking), (everyone.CannotRead[0], audience.CannotRead[0]));
            Assert.Empty(Member(audience, current).CannotRead);
            Assert.Equal([0], Member(audience, expired).CannotRead);
            Assert.Equal([0], Member(audience, future).CannotRead);
            Assert.Equal([AudienceReasonKind.Everyone], Member(audience, expired).Reasons.Select(static reason => reason.Kind));

            // --- The inactive role's grant reaches no one ---
            AudienceGrant share = Assert.Single(audience.Grants, grant => grant.RoleId == inactive);
            Assert.Equal((0, 0), (share.Members, share.CannotRead[0]));

            // --- A public role that reads the roles makes every audience user a reader ---
            int open = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {open};");
            try
            {
                ShareAudience opened = await ResolveAsync(ownerSubject, id, [RoleRead]);

                Assert.Equal(0, opened.CannotRead[0]);
                Assert.All(opened.Grants, static grant => Assert.Equal(0, grant.CannotRead[0]));
                Assert.All(opened.Members!, static member => Assert.Empty(member.CannotRead));
            }
            finally
            {
                await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[Roles] SET [IsPublic] = 0 WHERE [Id] = {open};");
            }
        }

        // --- Helpers ---

        /// <summary>The reports' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Report, int> Reports(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Report, int>>();
        }

        /// <summary>The ids of the related rows of one entity.</summary>
        /// <param name="audience">The audience.</param>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The ids.</returns>
        private static IEnumerable<int> RelatedIds(ShareAudience audience, string entity)
        {
            return audience.Related.Sets.TryGetValue(entity, out RelatedEntitySet? set) ? set.Entities.Cast<Entity<int>>().Select(static row => row.Id) : [];
        }

        /// <summary>Seeds a caller who reads the roles, so it sees the members.</summary>
        /// <param name="name">The caller's name.</param>
        /// <returns>The id and the subject.</returns>
        private async Task<(int UserId, string Subject)> OwnerAsync(string name)
        {
            (int user, string subject) = await fixture.SeedCallerAsync(name);
            await fixture.SeedMembershipAsync(user, await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null)));
            return (user, subject);
        }

        /// <summary>Creates a report as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="title">The title.</param>
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

        /// <summary>Shares a report with users as a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="id">The report.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the action does.</returns>
        private async Task ShareAsync(string subject, int id, ShareArguments arguments)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await Reports(scope).ExecuteActionAsync(ShareableNames.ShareAction, [id], arguments, new ActionOptions(), Token);
        }

        /// <summary>
        ///     Grants a report to a role or to everyone, bypassing the publish action: a caller's stored
        ///     permission to publish would reach every report, and so every other test's audience.
        /// </summary>
        /// <param name="id">The report.</param>
        /// <param name="role">The role; null for the Everyone grant.</param>
        /// <param name="access">The grant's access.</param>
        /// <returns>A task that completes when the grant is seeded.</returns>
        private Task GrantAsync(int id, int? role, ShareAccess access)
        {
            int system = WellKnownIds.SystemUserId;
            string grantee = role is int roleId ? roleId.ToString(CultureInfo.InvariantCulture) : "NULL";
            return fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                $"INSERT INTO [core].[ReportShares] ([Id], [ReportId], [UserId], [RoleId], [Access], [SharedById], [SharedAt]) VALUES (NEXT VALUE FOR [core].[sq_ReportShares], {id}, NULL, {grantee}, '{access}', {system}, SYSDATETIMEOFFSET());");
        }

        /// <summary>One listed member of an audience.</summary>
        /// <param name="audience">The audience.</param>
        /// <param name="user">The member.</param>
        /// <returns>The member.</returns>
        private static AudienceMember Member(ShareAudience audience, int user)
        {
            return Assert.Single(audience.Members!, member => member.UserId == user);
        }

        /// <summary>Resolves a report's audience as a caller, seen through nothing.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="id">The report.</param>
        /// <param name="data">The data securables.</param>
        /// <param name="services">The platform instance; the fixture's own when null.</param>
        /// <returns>The audience.</returns>
        private async Task<ShareAudience> ResolveAsync(string subject, int id, IReadOnlyList<SecurableRef> data, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: services);
            return await scope.ServiceProvider.GetRequiredService<IAudienceResolver>().ResolveAsync(new AudienceQuery(Resource, id, data, null, null), Token);
        }

        /// <summary>
        ///     A permission's filter to the one report: an unfiltered permission would reach every
        ///     report, and so every other test's audience.
        /// </summary>
        /// <param name="id">The report.</param>
        /// <returns>The filter.</returns>
        private static string Only(int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Id = {id}");
        }

        /// <summary>How many audience users the first tenant has: active users other than the system user.</summary>
        /// <returns>The count.</returns>
        private async Task<int> AudienceUsersAsync()
        {
            return await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [core].[Users] WHERE [IsActive] = 1 AND [Kind] <> 'System';");
        }

        /// <summary>Seeds active individuals of the first tenant in one round trip, under fresh ids.</summary>
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

        /// <summary>Seeds a role with permissions whose current members are some users, in two round trips.</summary>
        /// <param name="users">The members.</param>
        /// <param name="permissions">The role's permissions.</param>
        /// <returns>The role.</returns>
        private async Task<int> RoleOfAsync(int[] users, params (string Resource, string Action, string? Filter)[] permissions)
        {
            int role = await fixture.SeedRoleAsync(permissions);
            string rows = string.Join(", ", users.Select(user => string.Create(CultureInfo.InvariantCulture, $"(NEXT VALUE FOR [core].[sq_RoleMemberships], {user}, {role})")));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) VALUES {rows};");
            return role;
        }
    }
}
