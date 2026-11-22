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
    ///     The export and import records the reference distribution projects, each a real request
    ///     through the composed host: a member holding no grant on either resource queries, reads and
    ///     deletes its own records and never another member's, and its import whose job may still run
    ///     is refused its delete until the job ends.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ExportImportRecordTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_member_without_a_grant_reads_and_deletes_its_own_exports_and_never_another_members()
        {
            // Each record was last modified by the other member, so only its creator owns it.
            int me = UserId(deployment.Restricted);
            int other = UserId(deployment.Editor);
            int mine = await SeedExportAsync(me, modifiedBy: other);
            int theirs = await SeedExportAsync(other, modifiedBy: me);
            using SpaClient restricted = await deployment.SteppedUpAsync(deployment.Restricted);

            await AssertOwnRowsAsync(restricted, "exports", me, mine, theirs);

            // --- Another member's export is not found, as a read of it is, and stays; the member's own is deleted ---
            _ = (await restricted.PostAsync(Tenant, "exports/delete", Wire.Ids(theirs))).ExpectProblem(HttpStatusCode.NotFound, "not-found");
            _ = (await restricted.PostAsync(Tenant, "exports/delete", Wire.Ids(mine))).Expect(HttpStatusCode.OK);

            Assert.Equal((0, 1), (await CountAsync("Exports", mine), await CountAsync("Exports", theirs)));
        }

        [Fact]
        public async Task A_member_without_a_grant_reads_its_own_imports_and_deletes_one_only_once_its_job_has_ended()
        {
            // Each record was last modified by the other member, so only its creator owns it.
            int me = UserId(deployment.Restricted);
            int other = UserId(deployment.Editor);
            int live = await SeedImportAsync(me, other, "Held");
            int ended = await SeedImportAsync(me, other, "Succeeded");
            int theirs = await SeedImportAsync(other, me, "Succeeded");
            using SpaClient restricted = await deployment.SteppedUpAsync(deployment.Restricted);

            await AssertOwnRowsAsync(restricted, "imports", me, live, theirs);

            // --- The import whose job may still run is refused at its position; the one whose job ended is deleted ---
            JsonNode problem = (await restricted.PostAsync(Tenant, "imports/delete", Wire.Ids(live))).ExpectProblem(HttpStatusCode.UnprocessableEntity, "validation");
            Assert.Equal("Imports.JobActive", Assert.Single(problem["errors"]!.AsArray())!["code"]!.GetValue<string>());
            _ = (await restricted.PostAsync(Tenant, "imports/delete", Wire.Ids(ended))).Expect(HttpStatusCode.OK);

            Assert.Equal((1, 0, 1), (await CountAsync("Imports", live), await CountAsync("Imports", ended), await CountAsync("Imports", theirs)));
        }

        /// <summary>Asserts a member's query, details and by-ids reads answer its own records and never another member's.</summary>
        /// <param name="client">The member's client.</param>
        /// <param name="stack">The records' route prefix.</param>
        /// <param name="userId">The member's user id.</param>
        /// <param name="mine">One of the member's records.</param>
        /// <param name="theirs">Another member's record.</param>
        /// <returns>A task that completes when the reads are asserted.</returns>
        private static async Task AssertOwnRowsAsync(SpaClient client, string stack, int userId, int mine, int theirs)
        {
            // --- The query: every row the member's own, the seeded one among them ---
            List<JsonArray> rows = Wire.Rows((await client.PostAsync(Tenant, $"{stack}/query", Wire.Query("Id,CreatedById", filter: null))).Expect(HttpStatusCode.OK));
            Assert.Contains(mine, rows.Select(static row => row[0]!.GetValue<int>()));
            Assert.All(rows, row => Assert.Equal(userId, row[1]!.GetValue<int>()));

            // --- The details: the member's own record, and another member's not found ---
            JsonObject details = Assert.Single(Wire.Entities((await client.PostAsync(Tenant, $"{stack}/get", new JsonObject { ["id"] = mine })).Expect(HttpStatusCode.OK)));
            Assert.Equal(mine, details["id"]!.GetValue<int>());
            _ = (await client.PostAsync(Tenant, $"{stack}/get", new JsonObject { ["id"] = theirs })).ExpectProblem(HttpStatusCode.NotFound, "not-found");

            // --- The by-ids read: the member's own record alone ---
            Assert.Equal([mine], Wire.IdsOf((await client.PostAsync(Tenant, $"{stack}/get-by-ids", Wire.Ids(mine, theirs))).Expect(HttpStatusCode.OK)));
        }

        /// <summary>A member's user id in the live tenant.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The id.</returns>
        private static int UserId(Member member)
        {
            return member.UserId ?? throw new InvalidOperationException($"{member.Email} has no user in the live tenant.");
        }

        /// <summary>Seeds a display export record created by one user and last modified by another.</summary>
        /// <param name="userId">The creator.</param>
        /// <param name="modifiedBy">The last modifier.</param>
        /// <returns>The record's id.</returns>
        private async Task<int> SeedExportAsync(int userId, int modifiedBy)
        {
            return await deployment.ScalarAsync<int>(Tenant, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Exports];
                INSERT INTO [core].[Exports] ([Id], [Resource], [Kind], [ExpiresAt], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (@id, 'gl.Center', 'Display', DATEADD(day, 7, SYSUTCDATETIME()), SYSUTCDATETIME(), {userId}, SYSUTCDATETIME(), {modifiedBy});
                SELECT @id;
                """));
        }

        /// <summary>Seeds an import record created by one user and last modified by another, with its job in a status no worker claims.</summary>
        /// <param name="userId">The creator.</param>
        /// <param name="modifiedBy">The last modifier.</param>
        /// <param name="status">The job's status: held, or one that has ended.</param>
        /// <returns>The record's id.</returns>
        private async Task<int> SeedImportAsync(int userId, int modifiedBy, string status)
        {
            string completedAt = status == "Held" ? "NULL" : "SYSUTCDATETIME()";
            return await deployment.ScalarAsync<int>(Tenant, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @job int = NEXT VALUE FOR [core].[sq_Jobs];
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt], [CompletedAt])
                VALUES (@job, N'suite.probe', '{status}', SYSUTCDATETIME(), {userId}, SYSUTCDATETIME(), {completedAt});
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Imports];
                INSERT INTO [core].[Imports] ([Id], [Resource], [Mode], [ExpiresAt], [JobId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (@id, 'gl.Center', 'Insert', DATEADD(day, 7, SYSUTCDATETIME()), @job, SYSUTCDATETIME(), {userId}, SYSUTCDATETIME(), {modifiedBy});
                SELECT @id;
                """));
        }

        /// <summary>How many rows of a core table hold an id: one while the record stands, none once deleted.</summary>
        /// <param name="table">The table.</param>
        /// <param name="id">The id.</param>
        /// <returns>The count.</returns>
        private async Task<int> CountAsync(string table, int id)
        {
            return await deployment.ScalarAsync<int>(Tenant, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[{table}] WHERE [Id] = {id};"));
        }
    }
}
