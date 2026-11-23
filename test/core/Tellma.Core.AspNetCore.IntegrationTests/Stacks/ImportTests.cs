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
    ///     The import inspections and imports the reference distribution projects for centers, users
    ///     and roles, each a real request through the composed host: an editable export of a row is
    ///     uploaded as the caller's import file, its inspection answers the plan that maps it back,
    ///     same-source, every column mapped, and its import updates the row it came from. An inspection
    ///     or an import of an upload the caller does not hold is not found.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ImportTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>The blob kind of an import's upload.</summary>
        private const string ImportFile = "import-file";

        /// <summary>The content type of a workbook.</summary>
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        [Fact]
        public async Task Every_center_user_and_role_inspection_maps_its_own_editable_export_back()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);

            // --- The rows to export: a center, a role and a user holding the role ---
            int center = await CenterOperationTests.CreateAsync(admin, "IM-C", "Imported center", await CenterOperationTests.RootAsync(admin));
            int role = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Imported role",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }))).Expect(HttpStatusCode.OK)));
            int user = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["email"] = "imported@example.com",
                ["name"] = "Imported User",
                ["roleMemberships"] = new JsonArray(new JsonObject { ["id"] = 0, ["roleId"] = role }),
            }))).Expect(HttpStatusCode.OK)));

            // --- Each stack's own export, uploaded and inspected: the parent sheet first, every column mapped ---
            foreach ((string stack, int id, string entity) in new[] { ("centers", center, "gl.Center"), ("roles", role, "core.Role"), ("users", user, "core.User") })
            {
                ApiResponse exported = await admin.PostAsync(Tenant, $"{stack}/export-for-import", new JsonObject { ["ids"] = Wire.Array(id) });
                Assert.True(exported.Status == HttpStatusCode.OK, exported.ToString());
                int fileId = (await admin.UploadAsync(Tenant, ImportFile, exported.Body, Xlsx, $"{stack}.xlsx")).Expect(HttpStatusCode.Created)["id"]!.GetValue<int>();

                JsonNode plan = (await admin.PostAsync(Tenant, $"{stack}/inspect-import", new JsonObject { ["fileId"] = fileId })).Expect(HttpStatusCode.OK);

                Assert.Equal((entity, true, true), (plan["entity"]!.GetValue<string>(), plan["manifestPresent"]!.GetValue<bool>(), plan["sameSource"]!.GetValue<bool>()));
                JsonArray sheets = plan["sheets"]!.AsArray();
                Assert.Equal("Parent", sheets[0]!["status"]!.GetValue<string>());
                // A 64-bit count travels as a string, as every 64-bit integer on the wire does.
                Assert.Equal("1", sheets[0]!["rows"]!.GetValue<string>());
                Assert.All(
                    sheets.Where(static sheet => sheet!["status"]!.GetValue<string>() is "Parent" or "Child").SelectMany(static sheet => sheet!["columns"]!.AsArray()),
                    static column => Assert.Equal("Mapped", column!["status"]!.GetValue<string>()));
                Assert.Equal("Id", plan["defaultRowKey"]!.GetValue<string>());
            }

            // --- An upload the caller does not hold ---
            _ = (await admin.PostAsync(Tenant, "centers/inspect-import", new JsonObject { ["fileId"] = int.MaxValue })).ExpectProblem(HttpStatusCode.NotFound, "not-found");
        }

        [Fact]
        public async Task Every_center_user_and_role_imports_its_own_editable_export_back_by_id()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);

            // --- The rows to export: a center, a role and a user holding the role ---
            int center = await CenterOperationTests.CreateAsync(admin, "IM-U", "Updated center", await CenterOperationTests.RootAsync(admin));
            int role = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Updated role",

                // The membership sheet names its role by the role's natural key, so the role has one.
                ["code"] = "IM-R",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }))).Expect(HttpStatusCode.OK)));
            int user = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["email"] = "updated@example.com",
                ["name"] = "Updated User",
                ["roleMemberships"] = new JsonArray(new JsonObject { ["id"] = 0, ["roleId"] = role }),
            }))).Expect(HttpStatusCode.OK)));

            // --- Each stack's own export, uploaded and imported back unchanged: one row updated, by id ---
            foreach ((string stack, int id) in new[] { ("centers", center), ("roles", role), ("users", user) })
            {
                ApiResponse exported = await admin.PostAsync(Tenant, $"{stack}/export-for-import", new JsonObject { ["ids"] = Wire.Array(id) });
                Assert.True(exported.Status == HttpStatusCode.OK, exported.ToString());
                int fileId = (await admin.UploadAsync(Tenant, ImportFile, exported.Body, Xlsx, $"{stack}.xlsx")).Expect(HttpStatusCode.Created)["id"]!.GetValue<int>();

                ApiResponse imported = await admin.PostAsync(Tenant, $"{stack}/import", new JsonObject { ["fileId"] = fileId, ["mode"] = "Update" });
                Assert.True(imported.Status == HttpStatusCode.OK, imported.ToString());
                JsonNode outcome = imported.Expect(HttpStatusCode.OK);

                Assert.Equal((0, 1), (outcome["inserted"]!.GetValue<int>(), outcome["updated"]!.GetValue<int>()));
            }

            // --- An upload the caller does not hold ---
            _ = (await admin.PostAsync(Tenant, "centers/import", new JsonObject { ["fileId"] = int.MaxValue })).ExpectProblem(HttpStatusCode.NotFound, "not-found");
        }
    }
}
