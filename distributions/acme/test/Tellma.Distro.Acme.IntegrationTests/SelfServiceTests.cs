// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     The client shell's boot calls on Acme's web host, as the signed-in administrator makes
    ///     them: the envelope, the locale change and the preference bag's write, their bodies bound
    ///     through the projected routes, the caller's tag in every answer's version-tags header, and
    ///     a bad key reported at its item on the wire.
    /// </summary>
    /// <param name="sql">The SQL Server the migrator and the host run against.</param>
    [Trait("Category", "Integration")]
    public sealed class SelfServiceTests(SqlServerFixture sql)
    {
        /// <summary>Acme's live seed tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The client header the SPA sends on every mutation.</summary>
        private const string ClientHeaderValue = "web/1.0";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_shell_s_boot_calls_answer_the_administrator_and_carry_the_caller_s_tag_in_the_header()
        {
            AcmeMigrator acme = new(sql, "acme_self_service");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // --- The envelope: the profile, the access summary and the four tags, the header agreeing ---
            using HttpResponseMessage me = await PostAsync(flow.Browser, "me", null);
            Assert.True(me.StatusCode == HttpStatusCode.OK, await Describe(me, host));
            MeResult envelope = await ReadAsync<MeResult>(me);
            Assert.Equal(AcmeSignIn.Administrator, envelope.User.Email);
            Assert.NotEmpty(envelope.Access.Securables);
            Assert.Equal(envelope.PreferencesTag, HeaderTag(me));
            Assert.Equal(envelope.Access.Tag, HeaderItems(me)[VersionTagWire.PermissionsItem]);

            // --- The locale change moves the caller's tag, and the header carries the moved one ---
            using HttpResponseMessage locale = await PostAsync(flow.Browser, "me/locale", new { timeZone = "Asia/Riyadh", digits = "Native" });
            Assert.True(locale.StatusCode == HttpStatusCode.OK, await Describe(locale, host));
            MeLocaleResult changed = await ReadAsync<MeLocaleResult>(locale);
            Assert.Equal(("Asia/Riyadh", (DigitStyle?)DigitStyle.Native), (changed.PreferredTimeZone, changed.PreferredDigits));
            Assert.NotEqual(envelope.PreferencesTag, changed.PreferencesTag);
            Assert.Equal(changed.PreferencesTag, HeaderTag(locale));

            // --- The bag's write, bound from { items }, answering the bag and the moved tag ---
            using HttpResponseMessage set = await PostAsync(flow.Browser, "me/preferences/set", new { items = new[] { new { key = "theme", value = "dark" } } });
            Assert.True(set.StatusCode == HttpStatusCode.OK, await Describe(set, host));
            PreferencesResult bag = await ReadAsync<PreferencesResult>(set);
            Assert.Equal("dark", bag.Preferences["theme"]);
            Assert.NotEqual(changed.PreferencesTag, bag.PreferencesTag);
            Assert.Equal(bag.PreferencesTag, HeaderTag(set));

            // --- A bad key, reported at its item on the wire ---
            using HttpResponseMessage refused = await PostAsync(flow.Browser, "me/preferences/set", new { items = new[] { new { key = "Report.exposure", value = "x" } } });
            Assert.True(refused.StatusCode == HttpStatusCode.UnprocessableEntity, await Describe(refused, host));
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
            JsonElement error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(("items[0].key", AccessCodes.PreferenceKeyInvalid), (error.GetProperty("path").GetString(), error.GetProperty("code").GetString()));

            // --- The envelope read again holds what the writes stored ---
            using HttpResponseMessage again = await PostAsync(flow.Browser, "me", null);
            MeResult after = await ReadAsync<MeResult>(again);
            Assert.Equal(("Asia/Riyadh", "dark", bag.PreferencesTag), (after.User.PreferredTimeZone, after.Preferences["theme"], after.PreferencesTag));
        }

        /// <summary>Posts to one of the users' own routes, with the client header the SPA sends.</summary>
        /// <param name="browser">A browser holding a session.</param>
        /// <param name="action">The action's route under the users' segment.</param>
        /// <param name="body">The body; none when null.</param>
        /// <returns>The response; the caller disposes it.</returns>
        private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string action, object? body)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(string.Create(CultureInfo.InvariantCulture, $"/{Tenant}/api/web/users/{action}"), UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            return await browser.SendAsync(request, Token);
        }

        /// <summary>Reads a successful answer with the platform's wire contract.</summary>
        /// <typeparam name="T">The answer's type.</typeparam>
        /// <param name="response">The response.</param>
        /// <returns>The answer.</returns>
        private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        {
            return await response.Content.ReadFromJsonAsync<T>(TellmaJson.Options, Token)
                ?? throw new InvalidOperationException($"The route answered no {typeof(T).Name}.");
        }

        /// <summary>The items of a response's version-tags header, by name.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The items.</returns>
        private static Dictionary<string, string> HeaderItems(HttpResponseMessage response)
        {
            string header = string.Join(",", response.Headers.GetValues(TellmaHeaders.VersionTags));
            return header
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static item => item.Split('=', 2))
                .ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);
        }

        /// <summary>The caller's preferences tag a response's version-tags header carries.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The tag.</returns>
        private static string HeaderTag(HttpResponseMessage response)
        {
            return HeaderItems(response)[VersionTagWire.PreferencesItem];
        }

        /// <summary>A response's status and body, with the host's warnings, for a failed assertion.</summary>
        /// <param name="response">The response.</param>
        /// <param name="host">The host that answered.</param>
        /// <returns>The text.</returns>
        private static async Task<string> Describe(HttpResponseMessage response, AcmeHost host)
        {
            return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}\n{host.Warnings}";
        }
    }
}
