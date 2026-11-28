// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.AspNetCore.Tests.Problems;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    /// <summary>
    ///     The headers an admitted web request answers with, success or problem: the resolved
    ///     language and calendar, the version tags after its last batch and, where enabled, the
    ///     database's share of the time. A request refused before admission carries none of them,
    ///     and a problem renders in the request's language, except the one answer that must not
    ///     tell a missing tenant from a refused one.
    /// </summary>
    /// <param name="fixture">The host the cases share.</param>
    public sealed class ResponseHeadersTests(ResponseHeadersTests.Host fixture) : IClassFixture<ResponseHeadersTests.Host>
    {
        /// <summary>The Arabic range the SPA sends for a reader in Saudi Arabia with Latin digits.</summary>
        private const string Arabic = "ar-SA-u-nu-latn";

        /// <summary>The tenant's tags every tagged answer reports.</summary>
        private static readonly Dictionary<string, VersionTag> TenantTags = new(StringComparer.Ordinal)
        {
            [VersionTagNames.Settings] = VersionTag.New(),
            [VersionTagNames.Permissions] = VersionTag.New(),
            [VersionTagNames.Entities] = VersionTag.New(),
        };

        /// <summary>The tag both of a user's tags carry.</summary>
        private static readonly VersionTag UserTag = VersionTag.New();

        [Fact]
        public async Task An_admitted_answer_names_its_language_and_calendar()
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage response = await host.Client.SendAsync(Probe(host.Cookie()), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
            Assert.False(string.IsNullOrEmpty(Assert.Single(response.Headers.GetValues(TellmaHeaders.Calendar))));
        }

        [Fact]
        public async Task A_problem_after_admission_carries_the_same_headers()
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage response = await ProblemMappingTests.ThrowAsync(host, "not-found");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
            Assert.True(response.Headers.Contains(TellmaHeaders.Calendar));
        }

        [Fact]
        public async Task A_routed_platform_service_refused_for_its_body_answers_the_mapped_problem_with_the_same_headers()
        {
            TellmaTestHost host = fixture.Started;

            // The query discovery action, its request refused for a missing body before the service runs.
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/queryex/discover", host.Cookie(), "null");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("bad-request", problem.Code);
            Assert.StartsWith("The action 'discover' takes a body", problem.Document.GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
            Assert.True(response.Headers.Contains(TellmaHeaders.Calendar));
        }

        [Fact]
        public async Task A_refusal_before_admission_carries_none_of_them()
        {
            TellmaTestHost host = fixture.Started;

            // --- A cross-site write: refused by the forgery check ---
            using HttpRequestMessage forged = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", host.Cookie());
            forged.Headers.Remove("Sec-Fetch-Site");
            forged.Headers.Add("Sec-Fetch-Site", "cross-site");
            using HttpResponseMessage csrf = await host.Client.SendAsync(forged, TestContext.Current.CancellationToken);

            // --- A tenant that does not exist ---
            using HttpResponseMessage missing = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/99/api/web/probe", host.Cookie()), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.All([csrf, missing], static response =>
            {
                Assert.Empty(response.Content.Headers.ContentLanguage);
                Assert.False(response.Headers.Contains(TellmaHeaders.Calendar));
                Assert.False(response.Headers.Contains(TellmaHeaders.VersionTags));
                Assert.False(response.Headers.Contains("Server-Timing"));
            });
        }

        [Fact]
        public async Task An_offered_language_is_the_handlers_culture_and_the_answers_language()
        {
            TellmaTestHost host = fixture.Started;

            using HttpRequestMessage request = Probe(host.Cookie());
            request.Headers.Add("Accept-Language", Arabic);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ar-SA", (await ProbeResult.ReadAsync(response)).Culture);
            Assert.Equal(["ar"], response.Content.Headers.ContentLanguage);
        }

        [Fact]
        public async Task A_problem_renders_its_texts_in_the_requests_language()
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage english = await ProblemMappingTests.ThrowAsync(host, "not-found");
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/throw/not-found", host.Cookie());
            request.Headers.Add("Accept-Language", Arabic);
            using HttpResponseMessage arabic = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            string englishTitle = (await Problem.ReadAsync(english)).Document.GetProperty("title").GetString()!;
            string arabicTitle = (await Problem.ReadAsync(arabic)).Document.GetProperty("title").GetString()!;
            Assert.NotEqual(englishTitle, arabicTitle);
            Assert.Contains(arabicTitle, static c => c is >= '؀' and <= 'ۿ');
            Assert.Equal(["ar"], arabic.Content.Headers.ContentLanguage);
        }

        [Fact]
        public async Task A_missing_tenant_and_a_refused_member_answer_the_same_body_in_any_language()
        {
            TellmaTestHost host = fixture.Started;
            string stranger = host.Cookie(TellmaTestHost.Stranger);

            // --- A tenant that exists but the caller is no member of, and one that does not exist ---
            using HttpRequestMessage existing = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", stranger);
            using HttpRequestMessage absent = TellmaTestHost.SpaRequest(HttpMethod.Get, "/99/api/web/probe", stranger);
            existing.Headers.Add("Accept-Language", Arabic);
            absent.Headers.Add("Accept-Language", Arabic);
            using HttpResponseMessage refused = await host.Client.SendAsync(existing, TestContext.Current.CancellationToken);
            using HttpResponseMessage missing = await host.Client.SendAsync(absent, TestContext.Current.CancellationToken);

            // --- The same status, members and texts, the trace aside; no language either names ---
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Problem first = await Problem.ReadAsync(refused);
            Problem second = await Problem.ReadAsync(missing);
            Assert.Equal("tenant-not-found", first.Code);
            Assert.Equal(WithoutInstance(first), WithoutInstance(second));
            Assert.DoesNotContain(first.Document.GetProperty("title").GetString()!, static c => c is >= '؀' and <= 'ۿ');
            Assert.All([refused, missing], static response => Assert.Empty(response.Content.Headers.ContentLanguage));
        }

        [Fact]
        public async Task A_member_refused_inside_the_handler_answers_exactly_what_a_missing_tenant_answers()
        {
            // Server timing on and a batch already run: every header the stage could add has a value.
            await using TellmaTestHost host = await StartAsync(static options => options.Settings["Tellma:Api:ServerTiming"] = "true");
            string cookie = host.Cookie();

            // --- A member whose access ended after admission, refused by a batch in the handler ---
            using HttpRequestMessage inside = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/tagged/gone", cookie);
            inside.Headers.Add("Accept-Language", Arabic);
            using HttpResponseMessage refused = await host.Client.SendAsync(inside, TestContext.Current.CancellationToken);

            // --- A tenant that does not exist, refused by the guard ---
            using HttpRequestMessage absent = TellmaTestHost.SpaRequest(HttpMethod.Get, "/99/api/web/tagged/gone", cookie);
            absent.Headers.Add("Accept-Language", Arabic);
            using HttpResponseMessage missing = await host.Client.SendAsync(absent, TestContext.Current.CancellationToken);

            // --- The same status, headers and body, the trace aside ---
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Empty(refused.Content.Headers.ContentLanguage);
            Assert.False(refused.Headers.Contains(TellmaHeaders.Calendar));
            Assert.False(refused.Headers.Contains(TellmaHeaders.VersionTags));
            Assert.False(refused.Headers.Contains("Server-Timing"));
            Assert.Equal(Headers(missing), Headers(refused));
            Assert.Equal(WithoutInstance(await Problem.ReadAsync(missing)), WithoutInstance(await Problem.ReadAsync(refused)));
        }

        [Fact]
        public async Task The_version_tags_header_reports_the_last_batch_with_the_callers_tags_only_when_it_read_them()
        {
            TellmaTestHost host = fixture.Started;
            string cookie = host.Cookie();

            using HttpResponseMessage withUser = await host.Client.SendAsync(Tagged(cookie, "user"), TestContext.Current.CancellationToken);
            using HttpResponseMessage tenantOnly = await host.Client.SendAsync(Tagged(cookie, "tenant"), TestContext.Current.CancellationToken);
            using HttpResponseMessage otherUser = await host.Client.SendAsync(Tagged(cookie, "other-user"), TestContext.Current.CancellationToken);
            using HttpResponseMessage otherTenant = await host.Client.SendAsync(Tagged(cookie, "other-tenant"), TestContext.Current.CancellationToken);
            using HttpResponseMessage none = await host.Client.SendAsync(Probe(cookie), TestContext.Current.CancellationToken);

            // --- The caller's own tags add permissions and preferences; another user's never do ---
            VersionTagSnapshot tenant = new(1, TenantTags, DateTimeOffset.UnixEpoch);
            string full = VersionTagWire.HeaderValue(tenant, User(TellmaTestHost.UserId));
            string partial = VersionTagWire.HeaderValue(tenant, null);
            Assert.Equal(full, Assert.Single(withUser.Headers.GetValues(TellmaHeaders.VersionTags)));
            Assert.Equal(partial, Assert.Single(tenantOnly.Headers.GetValues(TellmaHeaders.VersionTags)));
            Assert.Equal(partial, Assert.Single(otherUser.Headers.GetValues(TellmaHeaders.VersionTags)));
            Assert.DoesNotContain("permissions=", partial, StringComparison.Ordinal);
            Assert.DoesNotContain("preferences=", partial, StringComparison.Ordinal);
            Assert.Contains("permissions=", full, StringComparison.Ordinal);

            // --- Another tenant's batch, or no batch at all: no header ---
            Assert.False(otherTenant.Headers.Contains(TellmaHeaders.VersionTags));
            Assert.False(none.Headers.Contains(TellmaHeaders.VersionTags));
        }

        [Fact]
        public async Task Server_timing_names_the_database_share_only_where_enabled()
        {
            TellmaTestHost production = fixture.Started;
            await using TellmaTestHost enabled = await StartAsync(static options => options.Settings["Tellma:Api:ServerTiming"] = "true");
            await using TellmaTestHost development = await StartAsync(static options => options.Environment = "Development");

            using HttpResponseMessage hidden = await production.Client.SendAsync(Tagged(production.Cookie(), "tenant"), TestContext.Current.CancellationToken);
            using HttpResponseMessage shown = await enabled.Client.SendAsync(Tagged(enabled.Cookie(), "tenant"), TestContext.Current.CancellationToken);
            using HttpResponseMessage local = await development.Client.SendAsync(Tagged(development.Cookie(), "tenant"), TestContext.Current.CancellationToken);

            Assert.False(hidden.Headers.Contains("Server-Timing"));
            Assert.Equal("db;dur=12.5;desc=\"3 calls\"", Assert.Single(shown.Headers.GetValues("Server-Timing")));
            Assert.Equal("db;dur=12.5;desc=\"3 calls\"", Assert.Single(local.Headers.GetValues("Server-Timing")));
        }

        /// <summary>Starts a host offering English and Arabic whose web surface maps the throwing and tagging endpoints.</summary>
        /// <param name="configure">More changes to the host.</param>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null)
        {
            return ProblemMappingTests.StartAsync(options =>
            {
                options.Compose = static tellma => tellma.Languages(["en", "ar"]);
                Action<WebApplication, TellmaEndpoints>? prior = options.Map;
                options.Map = (app, endpoints) =>
                {
                    prior?.Invoke(app, endpoints);
                    _ = endpoints.Web.MapGet("/tagged/{shape}", RecordBatch).WithMutation(false).AllowMember();
                };
                configure?.Invoke(options);
            });
        }

        /// <summary>Leaves an executed batch's outcome on the request's counters, as the executor does.</summary>
        /// <param name="shape">
        ///     Whose tags the batch read: <c>user</c>, <c>tenant</c>, <c>other-user</c> or
        ///     <c>other-tenant</c>; <c>gone</c> runs one batch, then refuses the caller as the next
        ///     batch's prologue does once the caller's membership has ended.
        /// </param>
        /// <param name="access">The request's counters.</param>
        /// <returns>An empty answer.</returns>
        private static IResult RecordBatch(string shape, DataAccessScope access)
        {
            VersionTagSnapshot tenant = new(shape == "other-tenant" ? 2 : 1, TenantTags, DateTimeOffset.UnixEpoch);
            UserVersionTagSnapshot? user = shape switch
            {
                "user" => User(TellmaTestHost.UserId),
                "other-user" => User(TellmaTestHost.UserId + 1),
                _ => null,
            };
            access.LastOutcome = new BatchOutcome(tenant, user, RoundTrips: 1);
            access.RoundTrips = 3;
            access.DatabaseTime = TimeSpan.FromMilliseconds(12.5);
            return shape == "gone" ? throw new TenantNotFoundException(1) : Results.NoContent();
        }

        /// <summary>A user's tags in tenant 1.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The tags.</returns>
        private static UserVersionTagSnapshot User(int userId)
        {
            return new UserVersionTagSnapshot(1, userId, UserTag, UserTag, DateTimeOffset.UnixEpoch);
        }

        /// <summary>A probe request as the SPA sends it.</summary>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The request.</returns>
        private static HttpRequestMessage Probe(string cookie)
        {
            return TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie);
        }

        /// <summary>A request to the tagging endpoint.</summary>
        /// <param name="cookie">The session cookie.</param>
        /// <param name="shape">Whose tags the batch read.</param>
        /// <returns>The request.</returns>
        private static HttpRequestMessage Tagged(string cookie, string shape)
        {
            return TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/tagged/" + shape, cookie);
        }

        /// <summary>A response's headers, the per-request trace and date aside.</summary>
        /// <param name="response">The response.</param>
        /// <returns>Each header as <c>name: value</c>, in order.</returns>
        private static string[] Headers(HttpResponseMessage response)
        {
            return [.. response.Headers.Concat(response.Content.Headers)
                .Where(static header => header.Key is not TellmaHeaders.TraceId and not "Date")
                .Select(static header => header.Key + ": " + string.Join(",", header.Value))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>A problem's body without its trace, which differs per request.</summary>
        /// <param name="problem">The problem.</param>
        /// <returns>The body's other members, as JSON text.</returns>
        private static string WithoutInstance(Problem problem)
        {
            var members = problem.Document.EnumerateObject()
                .Where(static member => member.Name != "instance")
                .ToDictionary(static member => member.Name, static member => member.Value, StringComparer.Ordinal);
            return JsonSerializer.Serialize(members);
        }

        /// <summary>
        ///     One Production host for the cases that send requests and read the answers: its endpoints
        ///     keep no state, so each case's answer is its own. A case that turns server timing on or
        ///     changes the environment starts a host of its own.
        /// </summary>
        public sealed class Host : IAsyncLifetime
        {
            /// <summary>The host, once started.</summary>
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Started => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await StartAsync();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }
    }
}
