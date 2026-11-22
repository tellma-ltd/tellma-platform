// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Stacks
{
    /// <summary>
    ///     Every projected user operation end to end on the reference distribution's live tenant: an
    ///     administrator, stepped up as a user change requires, saving, reading, inviting, deactivating,
    ///     reactivating and deleting users, reading and changing another user's preferences and
    ///     notification settings and issuing a service account's credentials; and a member reading and
    ///     changing their own profile, locale and preferences and sending themselves a test email. Each
    ///     case works on users of its own.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class UserOperationTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_user_saved_with_a_role_is_read_every_way_invited_deactivated_reactivated_and_deleted()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            int id = await CreateAsync(admin, "ops@example.com", "Operations User", deployment.EditorsRoleId);

            // --- Read every way: the query, the details with the membership, the details of many ---
            JsonNode query = (await admin.PostAsync(Tenant, "users/query", Wire.Query("Name,Email", "Email = 'ops@example.com'"))).Expect(HttpStatusCode.OK);
            JsonArray row = Assert.Single(Wire.Rows(query));
            Assert.Equal(("Operations User", "ops@example.com"), (row[0]!.GetValue<string>(), row[1]!.GetValue<string>()));
            JsonObject details = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "users/get", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK)));
            JsonNode membership = Assert.Single(details["roleMemberships"]!.AsArray())!;
            Assert.Equal(deployment.EditorsRoleId, membership["roleId"]!.GetValue<int>());
            Assert.Equal([id], Wire.IdsOf((await admin.PostAsync(Tenant, "users/get-by-ids", Wire.Ids(id))).Expect(HttpStatusCode.OK)));

            // --- The membership through the child query surface, by its user ---
            JsonNode memberships = (await admin.PostAsync(Tenant, "role-memberships/query", Wire.Query("RoleId", string.Create(CultureInfo.InvariantCulture, $"UserId = {id}"))))
                .Expect(HttpStatusCode.OK);
            Assert.Equal(deployment.EditorsRoleId, Assert.Single(Wire.Rows(memberships))[0]!.GetValue<int>());

            // --- Invited through the identity engine, and the invitation's status read back ---
            JsonNode invited = (await admin.PostAsync(Tenant, "users/invite", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.NotNull(Assert.Single(invited.AsArray())!["status"]);
            JsonNode status = (await admin.PostAsync(Tenant, "users/invitation-status", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            _ = Assert.Single(status.AsArray());
            Assert.False(string.IsNullOrEmpty(await deployment.ScalarAsync<string>(
                Tenant, string.Create(CultureInfo.InvariantCulture, $"SELECT [Subject] FROM [core].[Users] WHERE [Id] = {id};"))));

            // --- Deactivated, reactivated ---
            JsonNode deactivated = (await admin.PostAsync(Tenant, "users/deactivate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.False(Assert.Single(Wire.Entities(deactivated))["isActive"]!.GetValue<bool>());
            JsonNode activated = (await admin.PostAsync(Tenant, "users/activate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.True(Assert.Single(Wire.Entities(activated))["isActive"]!.GetValue<bool>());

            // --- Deleted ---
            int doomed = await CreateAsync(admin, "doomed@example.com", "Doomed User", roleId: null);
            _ = (await admin.PostAsync(Tenant, "users/delete", Wire.Ids(doomed))).Expect(HttpStatusCode.OK);
            Assert.Empty(Wire.IdsOf((await admin.PostAsync(Tenant, "users/get-by-ids", Wire.Ids(doomed))).Expect(HttpStatusCode.OK)));
        }

        [Fact]
        public async Task A_member_reads_and_changes_their_own_profile_locale_and_preferences()
        {
            Member self = await InvitedMemberAsync("self@example.com", "Self Service");
            using SpaClient client = await deployment.SignInAsync(self);

            // --- The envelope ---
            JsonNode me = (await client.PostAsync(Tenant, "users/me")).Expect(HttpStatusCode.OK);
            Assert.Equal((self.UserId, self.Email), (me["user"]!["id"]!.GetValue<int?>(), me["user"]!["email"]!.GetValue<string>()));

            // --- The profile ---
            // The edit is made against the stamp the envelope carried.
            JsonObject profile = new() { ["name"] = "Self Renamed", ["modifiedAt"] = me["user"]!["modifiedAt"]!.DeepClone() };
            JsonNode saved = (await client.PostAsync(Tenant, "users/me/save", profile)).Expect(HttpStatusCode.OK);
            Assert.Equal("Self Renamed", saved["user"]!["name"]!.GetValue<string>());

            // --- The locale ---
            JsonNode locale = (await client.PostAsync(Tenant, "users/me/locale", new JsonObject { ["timeZone"] = "Asia/Riyadh" })).Expect(HttpStatusCode.OK);
            Assert.Equal("Asia/Riyadh", locale["preferredTimeZone"]!.GetValue<string>());

            // --- The preference bag: set, then deleted ---
            JsonNode set = (await client.PostAsync(Tenant, "users/me/preferences/set", Items(("theme", "dark")))).Expect(HttpStatusCode.OK);
            Assert.Equal("dark", set["preferences"]!["theme"]!.GetValue<string>());
            JsonNode deleted = (await client.PostAsync(Tenant, "users/me/preferences/delete", Keys("theme"))).Expect(HttpStatusCode.OK);
            Assert.Null(deleted["preferences"]!["theme"]);
        }

        [Fact]
        public async Task An_administrator_reads_and_changes_another_user_s_preferences()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            int id = await CreateAsync(admin, "prefs@example.com", "Preferences User", roleId: null);

            JsonNode set = (await admin.PostAsync(Tenant, "users/preferences/set", new JsonObject { ["id"] = id, ["arguments"] = Items(("layout", "compact")) }))
                .Expect(HttpStatusCode.OK);
            Assert.Equal("compact", set["preferences"]!["layout"]!.GetValue<string>());
            JsonNode read = (await admin.PostAsync(Tenant, "users/preferences/get", new JsonObject { ["userId"] = id })).Expect(HttpStatusCode.OK);
            Assert.Equal("compact", read["preferences"]!["layout"]!.GetValue<string>());
            JsonNode deleted = (await admin.PostAsync(Tenant, "users/preferences/delete", new JsonObject { ["id"] = id, ["arguments"] = Keys("layout") }))
                .Expect(HttpStatusCode.OK);
            Assert.Null(deleted["preferences"]!["layout"]);
        }

        [Fact]
        public async Task A_member_sends_themselves_a_test_email_and_the_inbox_cannot_be_tested()
        {
            Member self = await InvitedMemberAsync("tester@example.com", "Test Sender");
            using SpaClient client = await deployment.SignInAsync(self);

            // --- The email channel: one uncorrelated message straight through the transport, to the member ---
            int before = TestEmails(self.Email);
            JsonNode sent = (await client.PostAsync(Tenant, "users/me/test-notification", new JsonObject { ["channel"] = "email" })).Expect(HttpStatusCode.OK);
            Assert.Equal("Sent", sent.GetValue<string>());
            Assert.Equal(before + 1, TestEmails(self.Email));

            // --- The inbox, which has no transport to test ---
            _ = (await client.PostAsync(Tenant, "users/me/test-notification", new JsonObject { ["channel"] = "inbox" }))
                .ExpectProblem(HttpStatusCode.UnprocessableEntity, "validation");
        }

        [Fact]
        public async Task An_administrator_reads_and_changes_another_user_s_notification_settings()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            int id = await CreateAsync(admin, "notices@example.com", "Notices User", roleId: null);

            // --- The save answers the stored deviation from the channel's default; the read answers it again ---
            JsonObject preference = new() { ["type"] = "core.user.added", ["channel"] = "email", ["enabled"] = true };
            JsonNode saved = (await admin.PostAsync(Tenant, "users/notification-preferences/save", new JsonObject
            {
                ["id"] = id,
                ["arguments"] = new JsonObject { ["preferences"] = new JsonArray(preference.DeepClone()) },
            })).Expect(HttpStatusCode.OK);
            Assert.True(JsonNode.DeepEquals(new JsonArray(preference.DeepClone()), saved), saved.ToJsonString());
            JsonNode read = (await admin.PostAsync(Tenant, "users/notification-preferences/get", new JsonObject { ["userId"] = id })).Expect(HttpStatusCode.OK);
            Assert.True(JsonNode.DeepEquals(new JsonArray(preference.DeepClone()), read), read.ToJsonString());
        }

        [Fact]
        public async Task A_service_account_is_issued_client_credentials_through_the_identity_engine()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            JsonNode saved = (await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["kind"] = "Service",
                ["name"] = "Suite Integration",
            }))).Expect(HttpStatusCode.OK);
            int id = Assert.Single(Wire.IdsOf(saved));

            JsonNode issued = (await admin.PostAsync(Tenant, "users/issue-credentials", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK);

            Assert.Equal(id, issued["id"]!.GetValue<int>());
            Assert.False(string.IsNullOrEmpty(issued["clientId"]!.GetValue<string>()));
            Assert.False(string.IsNullOrEmpty(issued["clientSecret"]!.GetValue<string>()));
        }

        /// <summary>Saves a new individual user, holding a role or none.</summary>
        /// <param name="admin">A stepped-up administrator.</param>
        /// <param name="email">The address.</param>
        /// <param name="name">The name.</param>
        /// <param name="roleId">The role the user holds; null for none.</param>
        /// <returns>The user's id.</returns>
        private static async Task<int> CreateAsync(SpaClient admin, string email, string name, int? roleId)
        {
            JsonObject user = new() { ["id"] = 0, ["email"] = email, ["name"] = name };
            if (roleId is int role)
            {
                user["roleMemberships"] = new JsonArray(new JsonObject { ["id"] = 0, ["roleId"] = role });
            }

            JsonNode saved = (await admin.PostAsync(Tenant, "users/save", Wire.Save(user))).Expect(HttpStatusCode.OK);
            return Assert.Single(Wire.IdsOf(saved));
        }

        /// <summary>A new user, saved and invited by the administrator, as the suite signs in as them.</summary>
        /// <param name="email">The address.</param>
        /// <param name="name">The name.</param>
        /// <returns>The member.</returns>
        private async Task<Member> InvitedMemberAsync(string email, string name)
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            int id = await CreateAsync(admin, email, name, roleId: null);
            _ = (await admin.PostAsync(Tenant, "users/invite", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            return await deployment.MemberAsync(Tenant, email);
        }

        /// <summary>The emails the host's transport took for an address outside the outbox: the test messages, which carry no correlation.</summary>
        /// <param name="address">The address.</param>
        /// <returns>Their count.</returns>
        private int TestEmails(string address)
        {
            return deployment.Host.Emails.Captured.Count(email => email.Message.Correlation is null
                && email.Message.To.Any(to => string.Equals(to.Address, address, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>A preference bag's write: <c>{ "items": [{ "key", "value" }] }</c>.</summary>
        /// <param name="items">The keys and values.</param>
        /// <returns>The body.</returns>
        private static JsonObject Items(params (string Key, string Value)[] items)
        {
            return new JsonObject { ["items"] = new JsonArray([.. items.Select(static item => new JsonObject { ["key"] = item.Key, ["value"] = item.Value })]) };
        }

        /// <summary>A preference bag's delete: <c>{ "keys": [...] }</c>.</summary>
        /// <param name="keys">The keys.</param>
        /// <returns>The body.</returns>
        private static JsonObject Keys(params string[] keys)
        {
            return new JsonObject { ["keys"] = new JsonArray([.. keys.Select(static key => JsonValue.Create(key))]) };
        }
    }
}
