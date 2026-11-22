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
    ///     Every projected report operation end to end on the reference distribution's live tenant: a
    ///     report the administrator saves untitled takes its default title and its creator as owner,
    ///     is read every way, shared with the editor who then reads it, published to everyone so the
    ///     restricted member reads it too, handed to the editor, who deactivates, reactivates and
    ///     deletes it, its grants going with it.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ReportOperationTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_report_is_saved_read_every_way_shared_published_transferred_deactivated_reactivated_and_deleted()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            using SpaClient editor = await deployment.SignInAsync(deployment.Editor);
            using SpaClient restricted = await deployment.SignInAsync(deployment.Restricted);
            int administrator = await deployment.AdministratorIdAsync(Tenant);
            int editorId = deployment.Editor.UserId!.Value;

            // --- Saved untitled: the default title, the creator as owner ---
            JsonNode saved = (await admin.PostAsync(Tenant, "reports/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["title"] = "",
                ["root"] = "gl.Center",
                ["kind"] = "List",
                ["definitionJson"] = /*lang=json,strict*/ """{ "formatVersion": 1, "list": { "columns": [ { "path": "Name" } ] } }""",
            }))).Expect(HttpStatusCode.OK);
            int id = Assert.Single(Wire.IdsOf(saved));
            JsonObject report = Assert.Single(Wire.Entities(saved));
            Assert.False(string.IsNullOrWhiteSpace(report["title"]!.GetValue<string>()));
            Assert.Equal(administrator, report["ownerId"]!.GetValue<int>());
            string byId = string.Create(CultureInfo.InvariantCulture, $"Id = {id}");

            // --- Read every way; the editor and the restricted member see nothing yet ---
            Assert.Single(Wire.Rows((await admin.PostAsync(Tenant, "reports/query", Wire.Query("Title", byId))).Expect(HttpStatusCode.OK)));
            JsonObject details = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "reports/get", new JsonObject { ["id"] = id })).Expect(HttpStatusCode.OK)));
            Assert.Equal([id], Wire.IdsOf((await admin.PostAsync(Tenant, "reports/get-by-ids", Wire.Ids(id))).Expect(HttpStatusCode.OK)));
            Assert.Empty(Wire.Rows((await editor.PostAsync(Tenant, "reports/query", Wire.Query("Title", byId))).Expect(HttpStatusCode.OK)));

            // --- Shared with the editor to view, then renamed by its owner ---
            _ = (await admin.PostAsync(Tenant, "reports/share", Arguments(id, new JsonObject
            {
                ["set"] = new JsonArray(new JsonObject { ["userId"] = editorId, ["access"] = "View" }),
            }))).Expect(HttpStatusCode.OK);
            Assert.Single(Wire.Rows((await editor.PostAsync(Tenant, "reports/query", Wire.Query("Title", byId))).Expect(HttpStatusCode.OK)));
            details["title"] = "Centers by name";
            JsonObject renamed = Assert.Single(Wire.Entities((await admin.PostAsync(Tenant, "reports/save", Wire.Save(details))).Expect(HttpStatusCode.OK)));
            Assert.Equal(("Centers by name", administrator), (renamed["title"]!.GetValue<string>(), renamed["ownerId"]!.GetValue<int>()));

            // --- Published to everyone: the restricted member reads it ---
            Assert.Empty(Wire.Rows((await restricted.PostAsync(Tenant, "reports/query", Wire.Query("Title", byId))).Expect(HttpStatusCode.OK)));
            _ = (await admin.PostAsync(Tenant, "reports/publish", Arguments(id, new JsonObject { ["everyone"] = true }))).Expect(HttpStatusCode.OK);
            Assert.Single(Wire.Rows((await restricted.PostAsync(Tenant, "reports/query", Wire.Query("Title", byId))).Expect(HttpStatusCode.OK)));

            // --- Handed to the editor, who deactivates, reactivates and deletes it ---
            _ = (await admin.PostAsync(Tenant, "reports/transfer", Arguments(id, new JsonObject { ["ownerId"] = editorId }))).Expect(HttpStatusCode.OK);
            Assert.Equal(editorId, await deployment.ScalarAsync<int>(Tenant, string.Create(CultureInfo.InvariantCulture, $"SELECT [OwnerId] FROM [core].[Reports] WHERE [Id] = {id};")));
            JsonNode deactivated = (await editor.PostAsync(Tenant, "reports/deactivate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.False(Assert.Single(Wire.Entities(deactivated))["isActive"]!.GetValue<bool>());
            JsonNode activated = (await editor.PostAsync(Tenant, "reports/activate", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.True(Assert.Single(Wire.Entities(activated))["isActive"]!.GetValue<bool>());
            _ = (await editor.PostAsync(Tenant, "reports/delete", Wire.Ids(id))).Expect(HttpStatusCode.OK);
            Assert.Empty(Wire.IdsOf((await admin.PostAsync(Tenant, "reports/get-by-ids", Wire.Ids(id))).Expect(HttpStatusCode.OK)));
            Assert.Equal(0, await deployment.ScalarAsync<int>(Tenant, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[ReportShares] WHERE [ReportId] = {id};")));
        }

        /// <summary>An action's request over one report.</summary>
        /// <param name="id">The report.</param>
        /// <param name="arguments">The action's arguments.</param>
        /// <returns>The request.</returns>
        private static JsonObject Arguments(int id, JsonObject arguments)
        {
            return new JsonObject { ["ids"] = Wire.Array(id), ["arguments"] = arguments };
        }
    }
}
