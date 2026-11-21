// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using Tellma.Core.Data;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Tenancy
{
    /// <summary>
    ///     A tenant request's data access runs under the request's aborted token: the scope's data
    ///     cancellation is that token, and a client that goes away cancels it.
    /// </summary>
    public sealed class RequestCancellationTests
    {
        /// <summary>The route of the endpoint that answers what the scope's data cancellation holds.</summary>
        private const string CancellationRoute = "/cancellation";

        /// <summary>The route of the endpoint that waits on the scope's data cancellation until it fires.</summary>
        private const string WaitRoute = "/wait";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_tenant_request_s_data_cancellation_is_the_request_s_aborted_token()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Map = static (_, endpoints) =>
                endpoints.Web.MapGet(CancellationRoute, static (HttpContext context, DataCancellation cancellation) =>
                    Results.Json(new CancellationProbe(cancellation.Token == context.RequestAborted, cancellation.Token.CanBeCanceled)))
                .WithMutation(false)
                .AllowMember());

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, $"/1/api/web{CancellationRoute}", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new CancellationProbe(true, true), await response.Content.ReadFromJsonAsync<CancellationProbe>(Token));
        }

        [Fact]
        public async Task A_client_that_goes_away_cancels_the_request_s_data_access()
        {
            TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                // The blocked read is ended by the client alone: the web timeout, a real timer the
                // cold host's first request could outrun on a loaded runner, is not what this pins.
                options.Settings["Tellma:Api:RequestTimeout"] = "01:00:00";
                options.Settings["Tellma:Api:LongRequestTimeout"] = "01:00:00";
                options.Map = (_, endpoints) => endpoints.Web.MapGet(WaitRoute, async (DataCancellation cancellation) =>
                {
                    // Stands in for a read blocked on the server: it waits on the scope's token alone.
                    using CancellationTokenRegistration registration = cancellation.Token.Register(() => cancelled.TrySetResult());
                    waiting.TrySetResult();
                    await cancelled.Task;
                    return Results.NoContent();
                })
                .WithMutation(false)
                .AllowMember();
            });

            // --- The client sends, waits until the endpoint is blocked, then goes away ---
            using var client = CancellationTokenSource.CreateLinkedTokenSource(Token);
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, $"/1/api/web{WaitRoute}", host.Cookie());
            Task<HttpResponseMessage> sent = host.Client.SendAsync(request, client.Token);
            await Patience.SignalledAsync(waiting.Task, sent);
            await client.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Patience.WaitAsync(sent));
            await Patience.WaitAsync(cancelled.Task);
        }

        /// <summary>What the cancellation endpoint answers.</summary>
        /// <param name="IsRequestAborted">Whether the scope's data cancellation is the request's aborted token.</param>
        /// <param name="CanBeCanceled">Whether that token can be cancelled at all.</param>
        private sealed record CancellationProbe(bool IsRequestAborted, bool CanBeCanceled);
    }
}
