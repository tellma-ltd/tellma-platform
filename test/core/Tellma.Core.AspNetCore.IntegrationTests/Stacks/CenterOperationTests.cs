// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Stacks
{
    /// <summary>
    ///     Every projected center operation end to end, as the administrator's browser calls it on the
    ///     reference distribution's live tenant: a tree saved with temporary ids and read back through
    ///     every read route, an edit from the details page and its stale resend, and the activation,
    ///     deactivation and both deletes. Each case works on centers of its own.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class CenterOperationTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_tree_saved_with_temporary_ids_is_read_back_through_every_read_route()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            int root = await RootAsync(admin);

            // --- The tree, its child naming its parent by the parent's temporary id ---
            JsonNode saved = (await admin.PostAsync(Tenant, "centers/save", Wire.Save(
                Center(-1, "CO-TREE", "Tree parent", "Abstract", root),
                Center(-2, "CO-TREE-1", "Tree child", "Sale", -1)))).Expect(HttpStatusCode.OK);
            List<int> ids = Wire.IdsOf(saved);
            Assert.Equal(2, ids.Count);
            Assert.All(ids, static id => Assert.True(id > 0));
            List<JsonObject> entities = Wire.Entities(saved);
            Assert.Equal((root, ids[0]), (entities[0]["parentId"]!.GetValue<int>(), entities[1]["parentId"]!.GetValue<int>()));

            // --- The query: the child with its parent's code, and the count in the same answer ---
            JsonNode query = (await admin.PostAsync(Tenant, "centers/query", Wire.Query("Code,Parent.Code,CenterType", "Code = 'CO-TREE-1'")))
                .Expect(HttpStatusCode.OK);
            JsonArray row = Assert.Single(Wire.Rows(query));
            Assert.Equal(("CO-TREE-1", "CO-TREE", "Sale"), (row[0]!.GetValue<string>(), row[1]!.GetValue<string>(), row[2]!.GetValue<string>()));
            Assert.Equal(1, query["count"]!.GetValue<int>());

            // --- The children of the parent, by parent id ---
            JsonNode children = (await admin.PostAsync(Tenant, "centers/get-by-parent-ids", new JsonObject { ["parentIds"] = Wire.Array(ids[0]) }))
                .Expect(HttpStatusCode.OK);
            Assert.Equal([ids[1]], Wire.Rows(children).Select(static cells => cells[0]!.GetValue<int>()));

            // --- The details of one, then of both ---
            JsonNode details = (await admin.PostAsync(Tenant, "centers/get", new JsonObject { ["id"] = ids[1] })).Expect(HttpStatusCode.OK);
            Assert.Equal("CO-TREE-1", Assert.Single(Wire.Entities(details))["code"]!.GetValue<string>());
            JsonNode both = (await admin.PostAsync(Tenant, "centers/get-by-ids", Wire.Ids(ids[0], ids[1]))).Expect(HttpStatusCode.OK);
            Assert.Equal(ids.Order(), Wire.IdsOf(both).Order());
        }

        [Fact]
        public async Task A_center_edited_from_its_details_saves_and_its_stale_resend_is_a_conflict_naming_the_editor()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            int id = await CreateAsync(admin, "CO-EDIT", "Before the edit", await RootAsync(admin));

            // --- The details payload, one property changed, saved back unchanged otherwise ---
            JsonObject before = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "centers/get", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK)));
            JsonObject edited = before.DeepClone().AsObject();
            edited["name"] = "After the edit";
            JsonNode saved = (await admin.PostAsync(Tenant, "centers/save", Wire.Save(edited))).Expect(HttpStatusCode.OK);
            JsonObject after = Assert.Single(Wire.Entities(saved));
            Assert.Equal("After the edit", after["name"]!.GetValue<string>());
            Assert.NotEqual(before["modifiedAt"]!.GetValue<string>(), after["modifiedAt"]!.GetValue<string>());

            // --- The same page resent with the stamp it read: a conflict naming who changed the row ---
            JsonObject stale = before.DeepClone().AsObject();
            stale["name"] = "A stale tab's edit";
            JsonNode conflict = (await admin.PostAsync(Tenant, "centers/save", Wire.Save(stale))).ExpectProblem(HttpStatusCode.Conflict, "concurrency-conflict");
            Assert.Contains(deployment.Administrator.Name, conflict.ToJsonString(), StringComparison.Ordinal);
            JsonObject unchanged = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "centers/get", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK)));
            Assert.Equal("After the edit", unchanged["name"]!.GetValue<string>());
        }

        [Fact]
        public async Task A_deactivated_center_leaves_the_default_query_until_activated_and_the_deletes_remove_it_and_a_subtree()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            int root = await RootAsync(admin);
            int leaf = await CreateAsync(admin, "CO-LEAF", "A leaf", root);
            JsonObject query = Wire.Query("Code", "Code = 'CO-LEAF'");
            JsonObject inactive = Wire.Query("Code", "Code = 'CO-LEAF'", includeInactive: true);

            // --- Deactivated: out of the default query, in the one that asks for inactive rows ---
            JsonNode deactivated = (await admin.PostAsync(Tenant, "centers/deactivate", Wire.Ids(leaf))).Expect(HttpStatusCode.OK);
            Assert.False(Assert.Single(Wire.Entities(deactivated))["isActive"]!.GetValue<bool>());
            Assert.Empty(Wire.Rows((await admin.PostAsync(Tenant, "centers/query", query)).Expect(HttpStatusCode.OK)));
            _ = Assert.Single(Wire.Rows((await admin.PostAsync(Tenant, "centers/query", inactive)).Expect(HttpStatusCode.OK)));

            // --- Activated: back in the default query ---
            JsonNode activated = (await admin.PostAsync(Tenant, "centers/activate", Wire.Ids(leaf))).Expect(HttpStatusCode.OK);
            Assert.True(Assert.Single(Wire.Entities(activated))["isActive"]!.GetValue<bool>());
            _ = Assert.Single(Wire.Rows((await admin.PostAsync(Tenant, "centers/query", query)).Expect(HttpStatusCode.OK)));

            // --- Deleted by id ---
            _ = (await admin.PostAsync(Tenant, "centers/delete", Wire.Ids(leaf))).Expect(HttpStatusCode.OK);
            Assert.Empty(Wire.IdsOf((await admin.PostAsync(Tenant, "centers/get-by-ids", Wire.Ids(leaf))).Expect(HttpStatusCode.OK)));

            // --- A parent deleted with its descendants ---
            List<int> subtree = Wire.IdsOf((await admin.PostAsync(Tenant, "centers/save", Wire.Save(
                Center(-1, "CO-SUB", "Subtree parent", "Abstract", root),
                Center(-2, "CO-SUB-1", "Subtree child", "Sale", -1)))).Expect(HttpStatusCode.OK));
            _ = (await admin.PostAsync(Tenant, "centers/delete-with-descendants", Wire.Ids(subtree[0]))).Expect(HttpStatusCode.OK);
            Assert.Empty(Wire.IdsOf((await admin.PostAsync(Tenant, "centers/get-by-ids", Wire.Ids([.. subtree]))).Expect(HttpStatusCode.OK)));
        }

        /// <summary>A new center as a details page sends it.</summary>
        /// <param name="id">Its temporary id, zero or negative.</param>
        /// <param name="code">Its code.</param>
        /// <param name="name">Its name.</param>
        /// <param name="type">Its type.</param>
        /// <param name="parentId">Its parent, a real or a temporary id.</param>
        /// <returns>The entity.</returns>
        internal static JsonObject Center(int id, string code, string name, string type, int? parentId)
        {
            return new JsonObject { ["id"] = id, ["code"] = code, ["name"] = name, ["centerType"] = type, ["parentId"] = parentId };
        }

        /// <summary>Saves a new sale center under a parent.</summary>
        /// <param name="client">The caller.</param>
        /// <param name="code">The code.</param>
        /// <param name="name">The name.</param>
        /// <param name="parentId">The parent.</param>
        /// <returns>The center's id.</returns>
        internal static async Task<int> CreateAsync(SpaClient client, string code, string name, int parentId)
        {
            ArgumentNullException.ThrowIfNull(client);
            JsonNode saved = (await client.PostAsync(Tenant, "centers/save", Wire.Save(Center(0, code, name, "Sale", parentId)))).Expect(HttpStatusCode.OK);
            return Assert.Single(Wire.IdsOf(saved));
        }

        /// <summary>The live tenant's root center, read as the tree's first level.</summary>
        /// <param name="client">The caller.</param>
        /// <returns>The root's id.</returns>
        internal static async Task<int> RootAsync(SpaClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            JsonNode roots = (await client.PostAsync(Tenant, "centers/get-by-parent-ids", new JsonObject { ["parentIds"] = new JsonArray(), ["includeRoots"] = true }))
                .Expect(HttpStatusCode.OK);
            JsonArray root = Assert.Single(Wire.Rows(roots));
            return root[0]!.GetValue<int>();
        }
    }
}
