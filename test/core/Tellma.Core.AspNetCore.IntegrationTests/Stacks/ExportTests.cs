// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Stacks
{
    /// <summary>
    ///     The exports the reference distribution projects for centers, users and roles and for their
    ///     child query surfaces, each a real request through the composed host: the answer is a
    ///     workbook streamed as an attachment, and it carries the rows the request selected. A
    ///     center, a role with a permission and a user holding that role are saved first, so each
    ///     export has a row of its own to find.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ExportTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task Every_center_user_and_role_export_streams_a_workbook_of_the_rows_it_selects()
        {
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);

            // --- The rows to find: a center, a role reading centers, and a user holding the role ---
            int center = await CenterOperationTests.CreateAsync(admin, "EX-C", "Exported center", await CenterOperationTests.RootAsync(admin));
            int role = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Exported role",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }))).Expect(HttpStatusCode.OK)));
            int user = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["email"] = "exported@example.com",
                ["name"] = "Exported User",
                ["roleMemberships"] = new JsonArray(new JsonObject { ["id"] = 0, ["roleId"] = role }),
            }))).Expect(HttpStatusCode.OK)));
            string byRole = string.Create(CultureInfo.InvariantCulture, $"RoleId = {role}");

            // --- Each stack's export by filter, and its export for import by id ---
            AssertWorkbook(await admin.PostAsync(Tenant, "centers/export", Export(["Code", "Name"], "Code = 'EX-C'")), "Exported center");
            AssertWorkbook(await admin.PostAsync(Tenant, "centers/export-for-import", new JsonObject { ["ids"] = Wire.Array(center) }), "Exported center");
            AssertWorkbook(await admin.PostAsync(Tenant, "roles/export", Export(["Name"], "Name = 'Exported role'")), "Exported role");
            AssertWorkbook(await admin.PostAsync(Tenant, "roles/export-for-import", new JsonObject { ["ids"] = Wire.Array(role) }), "Exported role");
            AssertWorkbook(await admin.PostAsync(Tenant, "users/export", Export(["Email", "Name"], "Email = 'exported@example.com'")), "exported@example.com");
            AssertWorkbook(await admin.PostAsync(Tenant, "users/export-for-import", new JsonObject { ["ids"] = Wire.Array(user) }), "exported@example.com");

            // --- The child query surfaces' exports, of the role's permission and of its membership ---
            AssertWorkbook(await admin.PostAsync(Tenant, "permissions/export", Export(["Resource", "Action"], byRole)), "gl.Center");
            AssertWorkbook(await admin.PostAsync(Tenant, "role-memberships/export", Export(["User.Email"], byRole)), "exported@example.com");
        }

        /// <summary>An export's body: the columns, each its own expression with no header of its own, and one filter.</summary>
        /// <param name="columns">The column expressions.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>The body.</returns>
        private static JsonObject Export(string[] columns, string filter)
        {
            return new JsonObject
            {
                ["columns"] = new JsonArray([.. columns.Select(static column => (JsonNode)new JsonObject { ["expression"] = column, ["header"] = null })]),
                ["filters"] = new JsonArray(filter),
            };
        }

        /// <summary>Asserts an answer is a workbook streamed as an attachment whose sheets hold a value.</summary>
        /// <param name="answer">The answer.</param>
        /// <param name="expected">A value one of the exported rows carries.</param>
        private static void AssertWorkbook(ApiResponse answer, string expected)
        {
            // --- A workbook, as an attachment ---
            Assert.True(answer.Status == HttpStatusCode.OK, answer.ToString());
            Assert.Equal(ActionAnswers.WorkbookContentType, answer.ContentHeaders.ContentType?.MediaType);
            Assert.Equal("attachment", answer.ContentHeaders.ContentDisposition?.DispositionType);

            // --- Its parts, a workbook's package, one of which holds the value: a sheet's own
            // inline strings or the shared strings every sheet refers to ---
            using ZipArchive package = new(new MemoryStream(answer.Body.ToArray()), ZipArchiveMode.Read);
            Assert.NotNull(package.GetEntry("xl/workbook.xml"));
            Assert.Contains(package.Entries.Where(static entry => entry.FullName.StartsWith("xl/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)), entry =>
            {
                using StreamReader part = new(entry.Open());
                return part.ReadToEnd().Contains(expected, StringComparison.Ordinal);
            });
        }
    }
}
