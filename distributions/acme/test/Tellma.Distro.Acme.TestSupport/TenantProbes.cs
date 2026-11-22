// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;

namespace Tellma.Distro.Acme.TestSupport
{
    /// <summary>
    ///     Three tenant endpoints a test maps onto the web group, as a distribution maps its own: a
    ///     read that runs one batch against the tenant's database and answers the tenant and the user
    ///     the request was bound to, a read whose batch waits on the server for a minute, and a
    ///     mutation that changes nothing and records that it ran. A state's refusal is decided before
    ///     any runs, so a refused mutation leaves no record.
    /// </summary>
    internal static class TenantProbes
    {
        /// <summary>The probes' route under the web group.</summary>
        private const string Route = "/probe";

        /// <summary>The slow read probe's route under the web group.</summary>
        private const string SlowRoute = "/probe/slow";

        /// <summary>The <c>Tellma-Client</c> value the SPA sends, which a cookie-authenticated mutation needs.</summary>
        private const string ClientHeaderValue = "web/1.0";

        /// <summary>Maps both probes.</summary>
        /// <param name="endpoints">The platform's groups.</param>
        public static void Map(TellmaEndpoints endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            _ = endpoints.Web.MapGet(Route, ServeReadAsync).WithMutation(false).AllowMember();
            _ = endpoints.Web.MapPost(Route, ServeWrite).WithMutation(true).AllowMember();
            _ = endpoints.Web.MapGet(SlowRoute, ServeSlowReadAsync).WithMutation(false).AllowMember();
        }

        /// <summary>Reads through the slow read probe of a tenant.</summary>
        /// <param name="browser">A browser holding a session.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The response; the caller disposes it.</returns>
        public static Task<HttpResponseMessage> SlowReadAsync(HttpClient browser, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(browser);
            return browser.GetAsync(Uri(tenantId, SlowRoute), TestContext.Current.CancellationToken);
        }

        /// <summary>Reads through the read probe of a tenant.</summary>
        /// <param name="browser">A browser holding a session.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The response; the caller disposes it.</returns>
        public static Task<HttpResponseMessage> ReadAsync(HttpClient browser, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(browser);
            return browser.GetAsync(Uri(tenantId, Route), TestContext.Current.CancellationToken);
        }

        /// <summary>Posts to the mutation probe of a tenant, with the client header the SPA sends.</summary>
        /// <param name="browser">A browser holding a session.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The response; the caller disposes it.</returns>
        public static async Task<HttpResponseMessage> WriteAsync(HttpClient browser, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(browser);
            using HttpRequestMessage request = new(HttpMethod.Post, Uri(tenantId, Route));
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            return await browser.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>Reads what the read probe answered.</summary>
        /// <param name="response">A successful response of the read probe.</param>
        /// <returns>The answer.</returns>
        public static async Task<TenantProbeResult> ResultAsync(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return await response.Content.ReadFromJsonAsync<TenantProbeResult>(TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The read probe answered nothing.");
        }

        /// <summary>
        ///     The problem code of a refusal's body, or the body itself when it carries none, so a
        ///     failed assertion shows what the host answered instead.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The code, or the body.</returns>
        public static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("code", out JsonElement code) && code.GetString() is string value ? value : body;
            }
            catch (JsonException)
            {
                return body;
            }
        }

        /// <summary>A probe's address in a tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="route">The probe's route under the web group.</param>
        /// <returns>The relative address.</returns>
        private static Uri Uri(int tenantId, string route)
        {
            return new Uri(string.Create(CultureInfo.InvariantCulture, $"/{tenantId}/api/web{route}"), UriKind.Relative);
        }

        /// <summary>
        ///     The read probe: one read batch against the tenant's database, the way a query reaches it,
        ///     then the tenant and the user the request was bound to.
        /// </summary>
        /// <param name="database">The request's tenant database.</param>
        /// <param name="accessor">The request's context.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The bound tenant and user, with the tenant's user count.</returns>
        private static async Task<IResult> ServeReadAsync(ITenantDatabase database, IRequestContextAccessor accessor, CancellationToken cancellationToken)
        {
            IDataBatch batch = database.CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> users = batch.Sql($"SELECT COUNT(*) AS [Count] FROM [core].[Users];", new SqlOptions(resultSets: 1));
            _ = await batch.ExecuteAsync(cancellationToken);

            RequestContext context = accessor.Current;
            int count = Convert.ToInt32(users.Value.ResultSets[0].GetBuffer(0).GetValue(0), CultureInfo.InvariantCulture);
            return Results.Json(new TenantProbeResult(context.RequireTenant().Id, context.UserId, count));
        }

        /// <summary>
        ///     The slow read probe: one read batch that waits a minute on the server, so only the
        ///     request's cancellation (its client going away, or its timeout) ends it sooner.
        /// </summary>
        /// <param name="database">The request's tenant database.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>No content, a minute later.</returns>
        private static async Task<IResult> ServeSlowReadAsync(ITenantDatabase database, CancellationToken cancellationToken)
        {
            IDataBatch batch = database.CreateBatch(BatchPurpose.Read);
            _ = batch.Sql($"WAITFOR DELAY '00:01:00';", SqlOptions.None);
            _ = await batch.ExecuteAsync(cancellationToken);
            return Results.NoContent();
        }

        /// <summary>The mutation probe: changes nothing and records that it ran.</summary>
        /// <param name="log">The host's record.</param>
        /// <returns>No content.</returns>
        private static IResult ServeWrite(TenantProbeLog log)
        {
            log.RecordWrite();
            return Results.NoContent();
        }
    }

    /// <summary>What the read probe answers.</summary>
    /// <param name="TenantId">The tenant the request was bound to.</param>
    /// <param name="UserId">The tenant user the request was bound to.</param>
    /// <param name="Users">How many rows the tenant's user table holds, as the read batch counted them.</param>
    internal sealed record TenantProbeResult(int TenantId, int? UserId, int Users);

    /// <summary>What one host's tenant probes recorded.</summary>
    internal sealed class TenantProbeLog
    {
        private int _writes;

        /// <summary>How many times the mutation probe ran.</summary>
        public int Writes => Volatile.Read(ref _writes);

        /// <summary>Records one run of the mutation probe.</summary>
        public void RecordWrite()
        {
            _ = Interlocked.Increment(ref _writes);
        }
    }
}
