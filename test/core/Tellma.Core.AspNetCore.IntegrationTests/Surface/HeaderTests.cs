// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.AspNetCore.IntegrationTests.Stacks;

namespace Tellma.Core.AspNetCore.IntegrationTests.Surface
{
    /// <summary>
    ///     The headers of the web surface's answers through the composed host: the trace id and the
    ///     build on every answer, tenantless ones and refusals before admission among them; the four
    ///     version tags on an admitted answer, agreeing with the envelope, and on none refused before
    ///     it; the contract check refusing an outdated client before anything runs; and the
    ///     compression and caching of an API answer.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class HeaderTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>The encodings the host compresses an answer with.</summary>
        private static readonly string[] Encodings = ["br", "gzip"];

        [Fact]
        public async Task An_admitted_answer_carries_the_four_version_tags_agreeing_with_the_envelope_and_the_request_s_headers()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            ApiResponse me = await admin.PostAsync(Tenant, "users/me");

            JsonNode envelope = me.Expect(HttpStatusCode.OK);
            List<KeyValuePair<string, string>> items = Tags(me);
            Assert.Equal(
                [VersionTagWire.SettingsItem, VersionTagWire.PermissionsItem, VersionTagWire.PreferencesItem, VersionTagWire.EntitiesItem],
                items.Select(static item => item.Key));
            Dictionary<string, string> tags = new(items, StringComparer.Ordinal);
            Assert.Equal(envelope["preferencesTag"]!.GetValue<string>(), tags[VersionTagWire.PreferencesItem]);
            Assert.Equal(envelope["access"]!["tag"]!.GetValue<string>(), tags[VersionTagWire.PermissionsItem]);
            AssertTraced(me);
            Assert.Equal("en", me.Header("Content-Language"));
            Assert.Equal("gc", me.Header(TellmaHeaders.Calendar));
            Assert.Equal("no-store", me.Header("Cache-Control"));
        }

        [Fact]
        public async Task A_tenantless_answer_and_refusals_before_admission_carry_the_trace_and_build_but_no_version_tags()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            using SpaClient anonymous = deployment.Anonymous();
            JsonObject read = new() { ["select"] = "Code", ["take"] = 1 };

            ApiResponse info = await anonymous.GetAsync("/api/distribution-info");
            ApiResponse csrf = await admin.PostAsync(Tenant, "centers/query", read, static request => request.Headers.Remove(TellmaHeaders.Client));
            ApiResponse outdated = await admin.PostAsync(Tenant, "centers/query", read, static request => request.Headers.Add(TellmaHeaders.Contract, "AAAAAAAAAAAA"));

            _ = info.Expect(HttpStatusCode.OK);
            _ = csrf.ExpectProblem(HttpStatusCode.Forbidden, "csrf-rejected");
            _ = outdated.ExpectProblem(HttpStatusCode.PreconditionFailed, "client-outdated");
            foreach (ApiResponse answer in new[] { info, csrf, outdated })
            {
                AssertTraced(answer);
                Assert.Null(answer.Header(TellmaHeaders.VersionTags));
            }
        }

        [Fact]
        public async Task An_outdated_contract_is_refused_before_anything_runs_while_the_current_one_and_none_pass()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            string fingerprint = deployment.Host.Services.GetRequiredService<IRouteContracts>()
                .For("POST /{tenantId}/api/web/centers/save")!.Fingerprint;
            int root = await CenterOperationTests.RootAsync(admin);

            // --- An outdated client's save: refused, and nothing written ---
            ApiResponse refused = await admin.PostAsync(
                Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "HD-OLD", "Outdated", "Sale", root)),
                static request => request.Headers.Add(TellmaHeaders.Contract, "AAAAAAAAAAAA"));
            _ = refused.ExpectProblem(HttpStatusCode.PreconditionFailed, "client-outdated");
            Assert.Null(refused.Header("Server-Timing"));
            Assert.Equal(0, await deployment.ScalarAsync<int>(Tenant, "SELECT COUNT(*) FROM [gl].[Centers] WHERE [Code] = N'HD-OLD';"));

            // --- The current fingerprint passes, and so does no header at all ---
            _ = (await admin.PostAsync(
                Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "HD-NEW", "Current", "Sale", root)),
                request => request.Headers.Add(TellmaHeaders.Contract, fingerprint))).Expect(HttpStatusCode.OK);
            _ = (await admin.PostAsync(Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "HD-NONE", "No header", "Sale", root))))
                .Expect(HttpStatusCode.OK);
        }

        [Fact]
        public async Task An_api_answer_is_compressed_when_the_browser_accepts_it_and_never_stored()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            ApiResponse me = await admin.PostAsync(Tenant, "users/me", configure: static request => request.Headers.Add("Accept-Encoding", "gzip, br"));

            Assert.Equal(HttpStatusCode.OK, me.Status);
            Assert.Contains(me.Header("Content-Encoding"), Encodings);
            Assert.Equal("no-store", me.Header("Cache-Control"));
        }

        /// <summary>Asserts an answer carries a trace id and the deployment's build.</summary>
        /// <param name="answer">The answer.</param>
        private static void AssertTraced(ApiResponse answer)
        {
            Assert.Matches("^[0-9a-f]{32}$", answer.Header(TellmaHeaders.TraceId) ?? string.Empty);
            Assert.False(string.IsNullOrEmpty(answer.Header(TellmaHeaders.Build)), answer.ToString());
        }

        /// <summary>The items of an answer's version-tags header, by name, in order.</summary>
        /// <param name="answer">The answer.</param>
        /// <returns>The items.</returns>
        private static List<KeyValuePair<string, string>> Tags(ApiResponse answer)
        {
            return [.. (answer.Header(TellmaHeaders.VersionTags) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static item => item.Split('=', 2))
                .Select(static pair => new KeyValuePair<string, string>(pair[0], pair[1]))];
        }
    }
}
