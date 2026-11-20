// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Data;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     The request timeouts through the real pipeline, lowered through configuration: a request
    ///     that outlives its policy answers 504 <c>timeout</c> when nothing was written, its data access
    ///     is cancelled with it, a long endpoint's own policy outlasts its group's, and a response the
    ///     timeout cuts short is aborted rather than ended as though it were whole.
    /// </summary>
    public sealed class RequestTimeoutTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_request_past_its_timeout_answers_504_and_cancels_its_data_access()
        {
            TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Settings["Tellma:Api:RequestTimeout"] = "00:00:00.300";
                options.Map = (_, endpoints) => endpoints.Web.MapGet("/slow", async (DataCancellation cancellation) =>
                {
                    // Stands in for a read blocked on the server: it waits on the scope's token alone.
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled.TrySetResult(true);
                        throw;
                    }

                    return Results.NoContent();
                })
                .WithMutation(false)
                .AllowMember();
            });

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/slow", host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal("timeout", (await Problem.ReadAsync(response)).Code);
            Assert.True(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
        }

        [Fact]
        public async Task A_long_endpoint_outlasts_its_groups_timeout()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Api:RequestTimeout"] = "00:00:00.200";
                options.Settings["Tellma:Api:LongRequestTimeout"] = "00:01:00";
                options.Map = static (app, endpoints) =>
                {
                    _ = endpoints.Web.MapGet("/long", static async (HttpContext context) =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(800), context.RequestAborted);
                        return Results.NoContent();
                    })
                    .WithMutation(false)
                    .AllowMember()
                    .WithRequestTimeout(TellmaRequestTimeouts.Long);
                };
            });

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/long", host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task A_response_the_timeout_cuts_short_is_aborted()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Api:RequestTimeout"] = "00:00:00.300";
                options.Map = static (_, endpoints) => endpoints.Web.MapGet("/partial", static async context =>
                {
                    // A stream that has begun: its status and first bytes are on the wire.
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("[1,", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                })
                .WithMutation(false)
                .AllowMember();
            });

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/partial", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _ = await Assert.ThrowsAnyAsync<Exception>(() => response.Content.ReadAsStringAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token));
        }

        [Fact]
        public async Task A_long_timeout_shorter_than_the_web_timeout_fails_startup()
        {
            // The options are judged where the host first reads them, mapping or the startup gate.
            Exception? error = await Record.ExceptionAsync(static async () =>
            {
                (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
                {
                    options.Settings["Tellma:Api:RequestTimeout"] = "00:01:00";
                    options.Settings["Tellma:Api:LongRequestTimeout"] = "00:00:30";
                });
                await using (app)
                {
                    await app.StartAsync(Token);
                }
            });

            Assert.NotNull(error);
            Assert.Contains("Tellma:Api:LongRequestTimeout is 00:00:30; it must be at least RequestTimeout", error.Message, StringComparison.Ordinal);
        }
    }
}
