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
    ///     Every projected role operation end to end on the reference distribution's live tenant, as a
    ///     stepped-up administrator calls them: a role saved with a permission, read every way and
    ///     through the permissions' child query surface, given a second permission from its details,
    ///     deactivated, reactivated and deleted; and the child surfaces as a member who may not read
    ///     roles sees them: the permissions refused, the memberships narrowed to the member's own.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class RoleOperationTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_role_saved_with_a_permission_is_read_every_way_edited_deactivated_reactivated_and_deleted()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);

            // --- Saved with one permission ---
            JsonNode saved = (await admin.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Suite readers",
                ["code"] = "suite-readers",
                ["permissions"] = new JsonArray(Permission("gl.Center", "Read")),
            }))).Expect(HttpStatusCode.OK);
            int id = Assert.Single(Wire.IdsOf(saved));
            string byRole = string.Create(CultureInfo.InvariantCulture, $"RoleId = {id}");

            // --- Read every way, and its permissions through the child query surface ---
            JsonNode query = (await admin.PostAsync(Tenant, "roles/query", Wire.Query("Code,Name", "Code = 'suite-readers'"))).Expect(HttpStatusCode.OK);
            Assert.Equal("Suite readers", Assert.Single(Wire.Rows(query))[1]!.GetValue<string>());
            JsonObject details = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "roles/get", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK)));
            _ = Assert.Single(details["permissions"]!.AsArray());
            Assert.Equal([id], Wire.IdsOf((await admin.PostAsync(Tenant, "roles/get-by-ids", Wire.Ids(id))).Expect(HttpStatusCode.OK)));
            JsonNode permissions = (await admin.PostAsync(Tenant, "permissions/query", Wire.Query("Resource,Action", byRole))).Expect(HttpStatusCode.OK);
            JsonArray permission = Assert.Single(Wire.Rows(permissions));
            Assert.Equal(("gl.Center", "Read"), (permission[0]!.GetValue<string>(), permission[1]!.GetValue<string>()));

            // --- A second permission added from the details page ---
            JsonObject edited = details.DeepClone().AsObject();
            edited["permissions"]!.AsArray().Add(Permission("core.Role", "Read"));
            JsonObject after = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "roles/save", Wire.Save(edited))).Expect(HttpStatusCode.OK)));
            Assert.Equal(2, after["permissions"]!.AsArray().Count);
            Assert.Equal(2, Wire.Rows((await admin.PostAsync(Tenant, "permissions/query", Wire.Query("Resource", byRole))).Expect(HttpStatusCode.OK)).Count);

            // --- Deactivated, reactivated ---
            JsonNode deactivated = (await admin.PostAsync(Tenant, "roles/deactivate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.False(Assert.Single(Wire.Entities(deactivated))["isActive"]!.GetValue<bool>());
            JsonNode activated = (await admin.PostAsync(Tenant, "roles/activate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.True(Assert.Single(Wire.Entities(activated))["isActive"]!.GetValue<bool>());

            // --- Deleted, with its permissions ---
            _ = (await admin.PostAsync(Tenant, "roles/delete", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.Empty(Wire.IdsOf((await admin.PostAsync(Tenant, "roles/get-by-ids", Wire.Ids(id))).Expect(HttpStatusCode.OK)));
            Assert.Empty(Wire.Rows((await admin.PostAsync(Tenant, "permissions/query", Wire.Query("Resource", byRole))).Expect(HttpStatusCode.OK)));
        }

        [Fact]
        public async Task A_child_query_surface_answers_its_parent_s_readers_and_refuses_a_member_who_may_not_read_the_parent()
        {
            string byRole = string.Create(CultureInfo.InvariantCulture, $"RoleId = {deployment.EditorsRoleId}");
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            using SpaClient restricted = await deployment.SignInAsync(deployment.Restricted);

            // --- The administrator reads the editors' role's permissions and memberships ---
            Assert.Equal(2, Wire.Rows((await admin.PostAsync(Tenant, "permissions/query", Wire.Query("Action", byRole))).Expect(HttpStatusCode.OK)).Count);
            List<JsonArray> members = Wire.Rows((await admin.PostAsync(Tenant, "role-memberships/query", Wire.Query("UserId", byRole))).Expect(HttpStatusCode.OK));
            Assert.Contains(deployment.Editor.UserId, members.Select(static row => row[0]!.GetValue<int?>()));

            // --- The restricted member, who reads centers alone, may not read roles, so the
            // permissions are refused; the memberships are owned by users, whose own row every member
            // reads, so the member is answered their own memberships alone: none of the editors' ---
            JsonNode refused = (await restricted.PostAsync(Tenant, "permissions/query", Wire.Query("Action", byRole))).ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
            Assert.Equal("no-grant", refused["arguments"]!["reason"]!.GetValue<string>());
            Assert.Empty(Wire.Rows((await restricted.PostAsync(Tenant, "role-memberships/query", Wire.Query("UserId", byRole))).Expect(HttpStatusCode.OK)));
            List<JsonArray> own = Wire.Rows((await restricted.PostAsync(Tenant, "role-memberships/query", Wire.Query("UserId", filter: null))).Expect(HttpStatusCode.OK));
            Assert.NotEmpty(own);
            Assert.All(own, row => Assert.Equal(deployment.Restricted.UserId, row[0]!.GetValue<int?>()));
        }

        /// <summary>A new permission as a role's details page sends it.</summary>
        /// <param name="resource">The securable's resource.</param>
        /// <param name="action">The securable's action.</param>
        /// <returns>The permission.</returns>
        private static JsonObject Permission(string resource, string action)
        {
            return new JsonObject { ["id"] = 0, ["resource"] = resource, ["action"] = action };
        }
    }
}
