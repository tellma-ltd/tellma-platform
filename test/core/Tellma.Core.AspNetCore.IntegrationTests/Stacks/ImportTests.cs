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
    ///     same-source, every column mapped, and its import updates the row it came from, keeping a
    ///     user's membership of a role that has no code. An inspection or an import of an upload the
    ///     caller does not hold is not found. Each background import start inserts the import's row
    ///     and answers it with the job that runs it, and refuses an upload the caller does not hold.
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

            // The role has no code: the membership sheet names it by its name, which every role has.
            int role = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Updated role",
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

            // --- The user still holds the role the file named by its name ---
            JsonObject details = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "users/get", new JsonObject { ["id"] = user })).Expect(HttpStatusCode.OK)));
            Assert.Equal(role, Assert.Single(details["roleMemberships"]!.AsArray())!["roleId"]!.GetValue<int>());

            // --- An upload the caller does not hold ---
            _ = (await admin.PostAsync(Tenant, "centers/import", new JsonObject { ["fileId"] = int.MaxValue })).ExpectProblem(HttpStatusCode.NotFound, "not-found");
        }

        [Fact]
        public async Task Every_center_user_and_role_import_start_inserts_its_import_row_and_hands_it_to_a_job()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);

            // --- Each stack's empty editable export, uploaded and started as a background import ---
            foreach (string stack in new[] { "centers", "roles", "users" })
            {
                ApiResponse exported = await admin.PostAsync(Tenant, $"{stack}/export-for-import", new JsonObject { ["ids"] = new JsonArray() });
                Assert.True(exported.Status == HttpStatusCode.OK, exported.ToString());
                int fileId = (await admin.UploadAsync(Tenant, ImportFile, exported.Body, Xlsx, $"{stack}-start.xlsx")).Expect(HttpStatusCode.Created)["id"]!.GetValue<int>();

                JsonNode accepted = (await admin.PostAsync(Tenant, $"{stack}/import/start", new JsonObject { ["fileId"] = fileId })).Expect(HttpStatusCode.Accepted);

                // --- The import's row, which names the job and owns the upload ---
                int importId = accepted["resourceId"]!.GetValue<int>();
                JsonObject row = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "imports/get", new JsonObject { ["id"] = importId })).Expect(HttpStatusCode.OK)));
                Assert.Equal((accepted["jobId"]!.GetValue<int>(), fileId), (row["jobId"]!.GetValue<int>(), row["fileId"]!.GetValue<int>()));
            }

            // --- An upload the caller does not hold: the row it would own is refused, and no job starts ---
            _ = (await admin.PostAsync(Tenant, "centers/import/start", new JsonObject { ["fileId"] = int.MaxValue })).ExpectProblem(HttpStatusCode.UnprocessableEntity, "validation");
        }
    }
}
