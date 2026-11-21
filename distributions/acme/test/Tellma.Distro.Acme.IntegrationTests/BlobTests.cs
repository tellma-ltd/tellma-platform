// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's blob endpoints as a developer's checkout runs them, on the Development file-system
    ///     store and the Skia processor: an avatar uploaded, attached by a user save, served with its
    ///     caching, conditionals and ranges, and gone once the save clears it; the upload's refusals
    ///     (a caller who is not a member, a missing client header, a declared length over the kind's
    ///     limit); and a read-only or frozen tenant refusing uploads while serving downloads.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class BlobTests(SqlServerFixture sql) : IDisposable
    {
        /// <summary>Why a blob the seeded administrator uploads cannot be asserted yet.</summary>
        private const string NeedsUserConnectionAndBootstrap =
            "Needs the user connection initializer, which binds a tenant request to its tenant user, and the administrator "
            + "bootstrap provisioning step, which makes the seeded administrator a user of the seed tenants.";

        /// <summary>The tenant every test uploads to: Acme's live seed tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The second seed tenant, where a test removes the administrator's membership.</summary>
        private const int OtherTenant = 2;

        /// <summary>The value the SPA sends in the client header on every write.</summary>
        private const string ClientHeaderValue = "web/1.0";

        /// <summary>
        ///     A one-pixel PNG: a real image, so the Skia processor normalises it to the avatar's
        ///     WebP square and renders its thumbnail.
        /// </summary>
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        // The test's own file-system store root, deleted when it ends; the migrator and every host share it.
        private readonly string _root = Path.Join(Path.GetTempPath(), "tellma-blobs-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12]);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task An_avatar_is_staged_attached_served_with_caching_and_ranges_and_gone_once_the_save_clears_it()
        {
            AcmeMigrator acme = await MigrateAsync("acme_blob_avatar");
            await using AcmeHost host = await StartAsync(acme);
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // --- The upload: 201, the staged blob, and its address ---
            using HttpResponseMessage upload = await UploadAsync(flow.Browser, Tenant, "me.png", Png);
            Assert.True(upload.StatusCode == HttpStatusCode.Created, await DescribeAsync(upload, host));
            JsonElement staged = await upload.Content.ReadFromJsonAsync<JsonElement>(Token);
            int id = staged.GetProperty("id").GetInt32();
            Assert.Equal("user-image", staged.GetProperty("kind").GetString());
            Assert.Equal("image/webp", staged.GetProperty("contentType").GetString());
            // Normalising crops to the avatar's square and never upscales, so the pixel stays one pixel.
            Assert.Equal(1, staged.GetProperty("width").GetInt32());
            Assert.Equal(1, staged.GetProperty("height").GetInt32());
            Assert.Equal("me.png", staged.GetProperty("fileName").GetString());
            string address = Address(Tenant, id);
            Assert.Equal(address, upload.Headers.Location?.ToString());

            // --- Before a save attaches it, only its uploader reads it, and nothing caches it ---
            using (HttpResponseMessage preview = await GetAsync(flow.Browser, address))
            {
                Assert.True(preview.StatusCode == HttpStatusCode.OK, await DescribeAsync(preview, host));
                Assert.Equal("private, no-store", CacheControl(preview));
            }

            // --- The save attaches it to the administrator ---
            int administrator = await AdministratorIdAsync(acme, Tenant);
            await SaveImageAsync(flow.Browser, host, administrator, id);

            // --- Committed: 200, its bytes, cached privately for a year, framed only by the platform, inline ---
            byte[] body;
            EntityTagHeaderValue tag;
            using (HttpResponseMessage served = await GetAsync(flow.Browser, address))
            {
                Assert.True(served.StatusCode == HttpStatusCode.OK, await DescribeAsync(served, host));
                body = await served.Content.ReadAsByteArrayAsync(Token);
                // The web API writes a 64-bit integer as a string, which a browser's numbers cannot lose precision in.
                Assert.Equal(long.Parse(staged.GetProperty("size").GetString()!, CultureInfo.InvariantCulture), body.Length);
                Assert.Equal(body.Length, served.Content.Headers.ContentLength);
                Assert.Equal("image/webp", served.Content.Headers.ContentType?.MediaType);
                Assert.Equal("private, max-age=31536000, immutable", CacheControl(served));
                Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"\"{id}\""), served.Headers.ETag?.Tag);
                Assert.False(served.Headers.ETag!.IsWeak);
                Assert.Equal(["frame-ancestors 'self'"], served.Headers.GetValues("Content-Security-Policy"));
                Assert.Equal(["nosniff"], served.Headers.GetValues("X-Content-Type-Options"));
                Assert.Equal(["bytes"], served.Headers.AcceptRanges);
                Assert.Equal("inline; filename*=UTF-8''me.png", served.Content.Headers.ContentDisposition?.ToString());
                tag = served.Headers.ETag;
            }

            // The stored primary is the file-system store's file under the tenant's directory.
            Assert.Contains(Directory.EnumerateFiles(Path.Join(_root, "t1"), "*", SearchOption.AllDirectories), path => new FileInfo(path).Length == body.Length);

            // --- The browser's held copy: 304, its tag and caching, and no body ---
            using (HttpRequestMessage conditional = new(HttpMethod.Get, address))
            {
                conditional.Headers.IfNoneMatch.Add(tag);
                using HttpResponseMessage unchanged = await flow.Browser.SendAsync(conditional, Token);
                Assert.True(unchanged.StatusCode == HttpStatusCode.NotModified, await DescribeAsync(unchanged, host));
                Assert.Equal(tag, unchanged.Headers.ETag);
                Assert.Equal("private, max-age=31536000, immutable", CacheControl(unchanged));
                Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync(Token));
            }

            // --- A satisfiable range: 206 and those bytes alone ---
            using (HttpRequestMessage ranged = new(HttpMethod.Get, address))
            {
                ranged.Headers.Range = new RangeHeaderValue(0, 9);
                using HttpResponseMessage partial = await flow.Browser.SendAsync(ranged, Token);
                Assert.True(partial.StatusCode == HttpStatusCode.PartialContent, await DescribeAsync(partial, host));
                Assert.Equal(body[..10], await partial.Content.ReadAsByteArrayAsync(Token));
                Assert.Equal(new ContentRangeHeaderValue(0, 9, body.Length), partial.Content.Headers.ContentRange);
            }

            // --- An unsatisfiable one: 416 naming the length ---
            using (HttpRequestMessage beyond = new(HttpMethod.Get, address))
            {
                beyond.Headers.Range = new RangeHeaderValue(body.Length + 100, null);
                using HttpResponseMessage refused = await flow.Browser.SendAsync(beyond, Token);
                Assert.True(refused.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, await DescribeAsync(refused, host));
                Assert.Equal(body.Length, refused.Content.Headers.ContentRange?.Length);
            }

            // --- The thumbnail, its own tag; and a download asked for as an attachment ---
            using (HttpResponseMessage thumbnail = await GetAsync(flow.Browser, address + "?variant=thumb"))
            {
                Assert.True(thumbnail.StatusCode == HttpStatusCode.OK, await DescribeAsync(thumbnail, host));
                Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"\"{id}-thumb\""), thumbnail.Headers.ETag?.Tag);
                Assert.Equal((await thumbnail.Content.ReadAsByteArrayAsync(Token)).Length, thumbnail.Content.Headers.ContentLength);
            }

            using (HttpResponseMessage attachment = await GetAsync(flow.Browser, address + "?download=true"))
            {
                Assert.True(attachment.StatusCode == HttpStatusCode.OK, await DescribeAsync(attachment, host));
                Assert.Equal("attachment; filename*=UTF-8''me.png", attachment.Content.Headers.ContentDisposition?.ToString());
            }

            // --- The save clears it: released, and the address answers 404 like any blob the caller may not read ---
            await SaveImageAsync(flow.Browser, host, administrator, imageId: null);
            using HttpResponseMessage gone = await GetAsync(flow.Browser, address);
            Assert.True(gone.StatusCode == HttpStatusCode.NotFound, await DescribeAsync(gone, host));
            Assert.Equal("not-found", await TenantProbes.ProblemCodeAsync(gone));
            Assert.Equal(["frame-ancestors 'self'"], gone.Headers.GetValues("Content-Security-Policy"));
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task An_upload_is_refused_to_a_non_member_without_the_client_header_and_over_the_kind_s_limit()
        {
            AcmeMigrator acme = await MigrateAsync("acme_blob_refusals");

            // The administrator stops being an active user of the second tenant before the host starts.
            await SqlServerFixture.ExecuteAsync(
                acme.TenantConnectionString(OtherTenant), $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Email] = N'{AcmeSignIn.Administrator}';");
            await using AcmeHost host = await StartAsync(acme);
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // --- A caller who is not a member is told nothing about the tenant ---
            using (HttpResponseMessage stranger = await UploadAsync(flow.Browser, OtherTenant, "me.png", Png))
            {
                await AssertProblemAsync(stranger, HttpStatusCode.NotFound, TenantProblemCodes.NotFound, host);
            }

            // --- A write without the client header is a forged one ---
            using (HttpResponseMessage forged = await UploadAsync(flow.Browser, Tenant, "me.png", Png, clientHeader: false))
            {
                await AssertProblemAsync(forged, HttpStatusCode.Forbidden, "csrf-rejected", host);
            }

            // --- A declared length over the avatar's 10 MiB is refused before the body is read ---
            CountingContent oversize = new(11L * 1024 * 1024);
            oversize.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            using (HttpRequestMessage request = new(HttpMethod.Post, UploadAddress(Tenant, "big.png")) { Content = oversize })
            {
                _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
                using HttpResponseMessage tooLarge = await flow.Browser.SendAsync(request, Token);
                await AssertProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "blob-rejected", host);
            }

            Assert.True(oversize.Produced < 1024 * 1024, $"The server read {oversize.Produced} bytes of a body it refused by its declared length.");
        }

        [Theory(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        [InlineData(TenantState.ReadOnly, TenantProblemCodes.ReadOnly)]
        [InlineData(TenantState.Frozen, TenantProblemCodes.Frozen)]
        public async Task A_locked_tenant_refuses_an_upload_with_its_code_and_still_serves_a_download(TenantState state, string code)
        {
            AcmeMigrator acme = await MigrateAsync("acme_blob_" + state.ToString().ToLowerInvariant());

            // --- While active: an avatar uploaded and attached ---
            string address;
            await using (AcmeHost active = await StartAsync(acme))
            {
                using OidcFlowClient flow = active.CreateFlowClient();
                await AcmeSignIn.SignInAsync(flow);
                using HttpResponseMessage upload = await UploadAsync(flow.Browser, Tenant, "me.png", Png);
                Assert.True(upload.StatusCode == HttpStatusCode.Created, await DescribeAsync(upload, active));
                int id = (await upload.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetInt32();
                await SaveImageAsync(flow.Browser, active, await AdministratorIdAsync(acme, Tenant), id);
                address = Address(Tenant, id);
            }

            // --- Locked: a frozen tenant's database is also set read-only, as the relocation runbook does ---
            await acme.SetStateAsync(Tenant, state, "locked");
            if (state == TenantState.Frozen)
            {
                await acme.SetDatabaseReadOnlyAsync(Tenant);
            }

            await using AcmeHost host = await StartAsync(acme);
            using OidcFlowClient locked = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(locked);

            using (HttpResponseMessage refused = await UploadAsync(locked.Browser, Tenant, "me.png", Png))
            {
                await AssertProblemAsync(refused, HttpStatusCode.Forbidden, code, host);
            }

            using HttpResponseMessage served = await GetAsync(locked.Browser, address);
            Assert.True(served.StatusCode == HttpStatusCode.OK, await DescribeAsync(served, host));
            Assert.NotEmpty(await served.Content.ReadAsByteArrayAsync(Token));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        /// <summary>
        ///     Migrates a deployment whose file-system store is rooted at the test's own directory, and
        ///     whose sensitive operations accept the email-code sign-in.
        /// </summary>
        /// <param name="name">The deployment's name.</param>
        /// <returns>The deployment.</returns>
        private async Task<AcmeMigrator> MigrateAsync(string name)
        {
            AcmeMigrator acme = new(sql, name);
            acme.Values["Tellma:Blobs:FileSystem:RootPath"] = _root;

            // A user save is sensitive; the email-code sign-in reaches the first tier only, so the bar is lowered to it.
            acme.Values["Tellma:Session:StepUp:Acr"] = "urn:tellma:acr:aal1";
            _ = await acme.MigrateAsync();
            return acme;
        }

        /// <summary>Starts a host on the store and processor Acme composes in Development.</summary>
        /// <param name="acme">The deployment.</param>
        /// <returns>The host.</returns>
        private static Task<AcmeHost> StartAsync(AcmeMigrator acme)
        {
            return AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { KeepsComposedBlobs = true });
        }

        /// <summary>A blob's download address.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="id">The blob.</param>
        /// <returns>The address.</returns>
        private static string Address(int tenantId, int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"/{tenantId}/blobs/user-image/{id}");
        }

        /// <summary>The avatar upload's address.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="fileName">The file's name.</param>
        /// <returns>The address.</returns>
        private static Uri UploadAddress(int tenantId, string fileName)
        {
            return new Uri(string.Create(CultureInfo.InvariantCulture, $"/{tenantId}/blobs/user-image?fileName={Uri.EscapeDataString(fileName)}"), UriKind.Relative);
        }

        /// <summary>Uploads a PNG as the administrator's avatar, as the SPA's upload sends it.</summary>
        /// <param name="browser">The signed-in browser.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="fileName">The file's name.</param>
        /// <param name="bytes">The file.</param>
        /// <param name="clientHeader">Whether the request carries the client header.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> UploadAsync(HttpClient browser, int tenantId, string fileName, byte[] bytes, bool clientHeader = true)
        {
            HttpRequestMessage request = new(HttpMethod.Post, UploadAddress(tenantId, fileName)) { Content = new ByteArrayContent(bytes) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            if (clientHeader)
            {
                _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            }

            return browser.SendAsync(request, Token);
        }

        /// <summary>Downloads a blob, as an <c>&lt;img&gt;</c> load does: no client header.</summary>
        /// <param name="browser">The signed-in browser.</param>
        /// <param name="address">The blob's address.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> GetAsync(HttpClient browser, string address)
        {
            return browser.GetAsync(new Uri(address, UriKind.Relative), Token);
        }

        /// <summary>
        ///     Saves the administrator's user record with an image id, as the details page does: the
        ///     record read in details shape, the one property changed, and the record saved back.
        /// </summary>
        /// <param name="browser">The signed-in browser.</param>
        /// <param name="host">The host, whose warnings a failure reports.</param>
        /// <param name="userId">The administrator's user id.</param>
        /// <param name="imageId">The blob to attach; null to clear the image.</param>
        /// <returns>A task that completes when the save succeeded.</returns>
        private static async Task SaveImageAsync(HttpClient browser, AcmeHost host, int userId, int? imageId)
        {
            // --- The record, in details shape ---
            JsonObject user;
            using (HttpResponseMessage read = await PostJsonAsync(browser, "users/get-by-ids", new JsonObject { ["ids"] = new JsonArray(userId) }))
            {
                Assert.True(read.StatusCode == HttpStatusCode.OK, await DescribeAsync(read, host));
                JsonNode? answer = await read.Content.ReadFromJsonAsync<JsonNode>(Token);
                user = answer?["entities"]?[0]?.AsObject() ?? throw new InvalidOperationException($"The read answered no user: {answer}");
            }

            // --- The one property changed, and the record saved back ---
            user["imageId"] = imageId;
            using HttpResponseMessage save = await PostJsonAsync(browser, "users/save", new JsonObject
            {
                ["entities"] = new JsonArray(user.DeepClone()),
                ["returnEntities"] = false,
            });
            Assert.True(save.IsSuccessStatusCode, await DescribeAsync(save, host));
        }

        /// <summary>Posts a JSON body to the tenant's web API, with the client header.</summary>
        /// <param name="browser">The signed-in browser.</param>
        /// <param name="route">The route after <c>api/web/</c>.</param>
        /// <param name="body">The body.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient browser, string route, JsonObject body)
        {
            HttpRequestMessage request = new(HttpMethod.Post, new Uri(string.Create(CultureInfo.InvariantCulture, $"/{Tenant}/api/web/{route}"), UriKind.Relative))
            {
                Content = JsonContent.Create(body),
            };
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            return browser.SendAsync(request, Token);
        }

        /// <summary>A response's <c>Cache-Control</c> as the server wrote it, not as the client reorders its directives.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The header's value.</returns>
        private static string CacheControl(HttpResponseMessage response)
        {
            return response.Headers.NonValidated["Cache-Control"].ToString();
        }

        /// <summary>Asserts that a response is a problem with a status and a code.</summary>
        /// <param name="response">The response.</param>
        /// <param name="status">The status.</param>
        /// <param name="code">The problem's code.</param>
        /// <param name="host">The host, whose warnings a failure reports.</param>
        /// <returns>A task that completes when the assertion ran.</returns>
        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, AcmeHost host)
        {
            string answered = await TenantProbes.ProblemCodeAsync(response);
            Assert.True(response.StatusCode == status && answered == code, $"{(int)response.StatusCode} {answered}\n{host.Warnings}");
        }

        /// <summary>A response's status and body, with the host's warnings, for a failed assertion.</summary>
        /// <param name="response">The response.</param>
        /// <param name="host">The host that answered.</param>
        /// <returns>The text.</returns>
        private static async Task<string> DescribeAsync(HttpResponseMessage response, AcmeHost host)
        {
            return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}\n{host.Warnings}";
        }

        /// <summary>The seeded administrator's user id in a tenant.</summary>
        /// <param name="acme">The deployment.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The id.</returns>
        private static async Task<int> AdministratorIdAsync(AcmeMigrator acme, int tenantId)
        {
            return await SqlServerFixture.ScalarAsync<int?>(
                acme.TenantConnectionString(tenantId), $"SELECT [Id] FROM [core].[Users] WHERE [Email] = N'{AcmeSignIn.Administrator}';")
                ?? throw new InvalidOperationException($"Tenant {tenantId} has no user {AcmeSignIn.Administrator}.");
        }

        /// <summary>
        ///     A body of a declared length that produces its bytes only as they are read, and counts
        ///     how many it produced: a refusal by the declared length leaves most of them unproduced.
        /// </summary>
        /// <param name="length">The declared length.</param>
        private sealed class CountingContent(long length) : HttpContent
        {
            /// <summary>How many bytes were produced.</summary>
            public long Produced { get; private set; }

            /// <inheritdoc />
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                // Chunks of 16 KiB until the declared length, or until the server stops reading.
                byte[] chunk = new byte[16 * 1024];
                while (Produced < length)
                {
                    int count = (int)Math.Min(chunk.Length, length - Produced);
                    await stream.WriteAsync(chunk.AsMemory(0, count), Token);
                    Produced += count;
                }
            }

            /// <inheritdoc />
            protected override bool TryComputeLength(out long declared)
            {
                declared = length;
                return true;
            }
        }
    }
}
