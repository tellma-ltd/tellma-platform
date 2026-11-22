// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.AspNetCore.IntegrationTests.Stacks;

namespace Tellma.Core.AspNetCore.IntegrationTests.Security
{
    /// <summary>
    ///     Who reaches what on the web surface, each a real request through the composed host: the
    ///     credentials it accepts and refuses, a stranger and a former member answered alike, the
    ///     sandbox's member kept to the sandbox, the grants that refuse a save or a delete and the
    ///     filter that refuses a save, an API action without its grant, the step-up a user change
    ///     requires, and the restricted member's navigation paths.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class AccessTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>A read every member of the live tenant who reads centers may make.</summary>
        private static JsonObject Read => new() { ["select"] = "Code", ["take"] = 1 };

        [Fact]
        public async Task A_stranger_and_a_former_member_receive_the_same_tenant_not_found_body()
        {
            using SpaClient stranger = await deployment.SignInAsync(deployment.Stranger);
            using SpaClient former = await deployment.SignInAsync(deployment.Former);

            JsonNode strangers = (await stranger.PostAsync(Tenant, "centers/query", Read)).ExpectProblem(HttpStatusCode.NotFound, TenantProblemCodes.NotFound);
            JsonNode formers = (await former.PostAsync(Tenant, "centers/query", Read)).ExpectProblem(HttpStatusCode.NotFound, TenantProblemCodes.NotFound);

            // Identical but for the trace id, so the answer tells a stranger nothing of who is a member.
            _ = strangers.AsObject().Remove("instance");
            _ = formers.AsObject().Remove("instance");
            Assert.Equal(strangers.ToJsonString(), formers.ToJsonString());
        }

        [Fact]
        public async Task The_sandbox_s_own_member_reaches_the_sandbox_and_not_the_live_tenant()
        {
            using SpaClient member = await deployment.SignInAsync(deployment.SandboxMember);

            _ = (await member.PostAsync(AcmeDeployment.SandboxTenant, "users/me")).Expect(HttpStatusCode.OK);
            _ = (await member.PostAsync(Tenant, "users/me")).ExpectProblem(HttpStatusCode.NotFound, TenantProblemCodes.NotFound);
        }

        [Fact]
        public async Task A_save_without_the_grant_a_delete_without_the_grant_and_a_save_outside_the_filter_are_refused_naming_no_role_or_filter()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            using SpaClient restricted = await deployment.SignInAsync(deployment.Restricted);
            using SpaClient editor = await deployment.SignInAsync(deployment.Editor);
            int root = await CenterOperationTests.RootAsync(admin);

            // --- Read alone: a save is refused for want of the grant ---
            JsonNode noSave = (await restricted.PostAsync(Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "AC-R", "Refused", "Sale", root))))
                .ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
            Assert.Equal(("Save", "no-grant"), Reason(noSave));

            // --- The editor's filtered save: a sale center is saved, a service center is refused as outside it ---
            int saved = await CenterOperationTests.CreateAsync(editor, "AC-E", "Edited by the editor", root);
            JsonNode outside = (await editor.PostAsync(Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "AC-O", "Outside", "Service", root))))
                .ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
            Assert.Equal(("Save", "outside-filter"), Reason(outside));

            // --- Save without Delete: the editor's own center is not deleted ---
            JsonNode noDelete = (await editor.PostAsync(Tenant, "centers/delete", Wire.Ids(saved))).ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
            Assert.Equal(("Delete", "no-grant"), Reason(noDelete));
            _ = Assert.Single(Wire.IdsOf((await admin.PostAsync(Tenant, "centers/get-by-ids", Wire.Ids(saved))).Expect(HttpStatusCode.OK)));

            // --- No refusal names the role that grants or the filter that limits ---
            foreach (JsonNode refusal in new[] { noSave, outside, noDelete })
            {
                string text = refusal.ToJsonString();
                Assert.DoesNotContain(AcmeDeployment.EditorsRoleName, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Center Readers", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CenterType", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_user_change_below_the_step_up_bar_is_refused_with_the_challenge_until_the_member_steps_up()
        {
            using SpaClient plain = await deployment.SignInAsync(deployment.Administrator);
            using SpaClient stale = new(deployment.Host, await WebSessions.SignInAsync(
                deployment.Host, deployment.Administrator, "urn:tellma:acr:aal2", authenticatedAgo: TimeSpan.FromMinutes(20)));
            using SpaClient stepped = await deployment.SteppedUpAsync(deployment.Administrator);
            JsonObject user = Wire.Save(new JsonObject { ["id"] = 0, ["email"] = "stepup@example.com", ["name"] = "Step Up" });

            // --- A sign-in below the bar, and one at the bar but too long ago ---
            foreach (SpaClient below in new[] { plain, stale })
            {
                ApiResponse refused = await below.PostAsync(Tenant, "users/save", user);
                _ = refused.ExpectProblem(HttpStatusCode.Unauthorized, "step-up-required");
                string challenge = refused.Header("WWW-Authenticate") ?? string.Empty;
                Assert.Contains("error=\"insufficient_user_authentication\"", challenge, StringComparison.Ordinal);
                Assert.Contains("acr_values=\"urn:tellma:acr:aal2\"", challenge, StringComparison.Ordinal);
                Assert.Contains("max_age=900", challenge, StringComparison.Ordinal);
            }

            Assert.Equal(0, await deployment.ScalarAsync<int>(Tenant, "SELECT COUNT(*) FROM [core].[Users] WHERE [Email] = N'stepup@example.com';"));

            // --- Stepped up moments ago: the same save succeeds ---
            _ = Assert.Single(Wire.IdsOf((await stepped.PostAsync(Tenant, "users/save", user)).Expect(HttpStatusCode.OK)));
        }

        [Fact]
        public async Task An_api_action_the_caller_holds_no_grant_for_is_refused_and_its_holder_is_served()
        {
            using SpaClient restricted = await deployment.SignInAsync(deployment.Restricted);
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            JsonObject another = new() { ["userId"] = deployment.Sales.UserId };

            // --- Another user's preferences need the users' preferences grant, which the member lacks ---
            JsonNode refused = (await restricted.PostAsync(Tenant, "users/preferences/get", another)).ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
            Assert.Equal("no-grant", refused["arguments"]!["reason"]!.GetValue<string>());
            Assert.DoesNotContain("Center Readers", refused.ToJsonString(), StringComparison.OrdinalIgnoreCase);

            // --- The administrator holds it ---
            _ = (await admin.PostAsync(Tenant, "users/preferences/get", another)).Expect(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_bearer_token_on_the_web_surface_and_a_request_without_a_session_are_refused()
        {
            using SpaClient anonymous = deployment.Anonymous();

            _ = (await anonymous.PostAsync(Tenant, "centers/query", Read, static request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJub25lIn0.e30.")))
                .ExpectProblem(HttpStatusCode.Unauthorized, "unsupported-credential");
            _ = (await anonymous.PostAsync(Tenant, "centers/query", Read)).ExpectProblem(HttpStatusCode.Unauthorized, "unauthenticated");
        }

        [Fact]
        public async Task An_unmapped_route_under_a_tenant_is_route_not_found()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            _ = (await admin.PostAsync(17, "x")).ExpectProblem(HttpStatusCode.NotFound, "route-not-found");
            _ = (await admin.PostAsync(Tenant, "centers/import")).ExpectProblem(HttpStatusCode.NotFound, "route-not-found");
        }

        [Fact]
        public async Task The_restricted_member_s_navigation_reaches_the_related_projection_alone_in_select_filter_and_order()
        {
            using SpaClient restricted = await deployment.SignInAsync(deployment.Restricted);
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            JsonObject[] beyond =
            [
                new() { ["select"] = "Code,Parent.CreatedAt", ["take"] = 2 },
                new() { ["select"] = "Code", ["filters"] = new JsonArray("Parent.CreatedAt is not null"), ["take"] = 2 },
                new() { ["select"] = "Code", ["orderBy"] = "Parent.CreatedAt desc", ["take"] = 2 },
            ];

            // --- Past the parent's related projection, wherever the path stands ---
            foreach (JsonObject query in beyond)
            {
                JsonNode refused = (await restricted.PostAsync(Tenant, "centers/query", query)).ExpectProblem(HttpStatusCode.Forbidden, "forbidden");
                Assert.Equal("restricted-path", refused["arguments"]!["reason"]!.GetValue<string>());
                Assert.Equal("Parent.CreatedAt", refused["arguments"]!["path"]!.GetValue<string>());
            }

            // --- Within it, the restricted member is served; the administrator reaches every column ---
            _ = (await restricted.PostAsync(Tenant, "centers/query", new JsonObject { ["select"] = "Code,Parent.Name", ["filters"] = new JsonArray("Parent.Name is not null"), ["orderBy"] = "Parent.Name", ["take"] = 2 }))
                .Expect(HttpStatusCode.OK);
            foreach (JsonObject query in beyond)
            {
                _ = (await admin.PostAsync(Tenant, "centers/query", query)).Expect(HttpStatusCode.OK);
            }
        }

        /// <summary>A forbidden problem's action and reason.</summary>
        /// <param name="problem">The problem's body.</param>
        /// <returns>The action and the reason.</returns>
        private static (string Action, string Reason) Reason(JsonNode problem)
        {
            return (problem["arguments"]!["action"]!.GetValue<string>(), problem["arguments"]!["reason"]!.GetValue<string>());
        }
    }
}
