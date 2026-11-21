// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Data;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     The request timeouts through the real pipeline, lowered through configuration or fired
    ///     while the endpoint runs: a request that outlives its policy answers 504 <c>timeout</c> when
    ///     nothing was written, its data access is cancelled with it, a long endpoint's own policy
    ///     outlasts its group's, and a response the timeout cuts short is aborted rather than ended as
    ///     though it were whole. A write sent before its timeout, which runs to its end and may commit,
    ///     and an answer whose body writer began after it, lose their connection rather than answer a
    ///     504, a 500 or a whole 200.
    /// </summary>
    /// <remarks>
    ///     A timer lowered through configuration starts at the timeout stage, ahead of the tenant front
    ///     door and the access guard, and the first request through a fresh host runs that stretch
    ///     cold: on a loaded runner it takes seconds. So a test whose scenario needs the endpoint
    ///     running when the timeout fires fires it from the endpoint, and a lowered timer is kept only
    ///     where the answer is the same wherever it fires.
    /// </remarks>
    public sealed class RequestTimeoutTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_request_past_its_configured_timeout_answers_504()
        {
            // The lowered timer may fire anywhere from the timeout stage on, before the endpoint runs
            // or while it waits: with nothing written, the answer is the same.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Api:RequestTimeout"] = "00:00:00.300";
                options.Map = static (_, endpoints) => endpoints.Web.MapGet("/slow", static async (DataCancellation cancellation) =>
                {
                    // Stands in for a read blocked on the server: it waits on the scope's token alone.
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                    return Results.NoContent();
                })
                .WithMutation(false)
                .AllowMember();
            });

            using HttpResponseMessage response = await Patience.WaitAsync(host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/slow", host.Cookie()), Token));

            // The setting reaches the web policy: the host's default outlasts the test's patience,
            // so a 504 from it never arrives in time, and the policy itself carries the setting.
            Assert.Equal(TimeSpan.FromMilliseconds(300), host.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value.Policies[TellmaRequestTimeouts.Web].Timeout);
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal("timeout", (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_timeout_that_fires_during_a_read_cancels_its_data_access_and_answers_504()
        {
            TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Map = (_, endpoints) => endpoints.Web.MapGet("/slow", async (HttpContext context, DataCancellation cancellation) =>
                {
                    // Stands in for a read blocked on the server: it waits on the scope's token alone,
                    // and the timeout fires once it waits.
                    using CancellationTokenRegistration registration = cancellation.Token.Register(() => cancelled.TrySetResult(true));
                    FireTimeout(context);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                    return Results.NoContent();
                })
                .WithMutation(false)
                .AllowMember();
            });

            using HttpResponseMessage response = await Patience.WaitAsync(host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/slow", host.Cookie()), Token));

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal("timeout", (await Problem.ReadAsync(response)).Code);
            Assert.True(await Patience.WaitAsync(cancelled.Task));
        }

        [Fact]
        public async Task A_long_endpoint_outlasts_its_groups_timeout()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                // The endpoint's long policy replaces its group's, so only the long timer is armed:
                // were the override lost, the 200 ms group timer would answer 504 within the
                // endpoint's 800 ms wait. The long policy only has to outlast the whole request,
                // however slowly a loaded runner serves it.
                options.Settings["Tellma:Api:RequestTimeout"] = "00:00:00.200";
                options.Settings["Tellma:Api:LongRequestTimeout"] = "01:00:00";
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

            using HttpResponseMessage response = await Patience.WaitAsync(host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/long", host.Cookie()), Token));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task A_response_the_timeout_cuts_short_is_aborted()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Map = static (_, endpoints) => endpoints.Web.MapGet("/partial", static async context =>
                {
                    // A stream that has begun: its status and first bytes are on the wire.
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("[1,", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);

                    // Its timeout fires only now, with the stream begun.
                    FireTimeout(context);
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                })
                .WithMutation(false)
                .AllowMember();
            });

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/partial", host.Cookie());
            using HttpResponseMessage response = await Patience.WaitAsync(host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token));

            // The abort reaches the reader: a read that only waits out the test's patience is a hang.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Patience.WaitAsync(response.Content.ReadAsStringAsync(Token)));
            Assert.IsNotType<TimeoutException>(failure);
        }

        [Fact]
        public async Task A_write_sent_before_its_timeout_whose_action_answer_comes_after_it_loses_its_connection()
        {
            TaskCompletionSource<bool> committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await StartAsync("/act", HttpMethod.Post, persists: true, committed, static () => ActionAnswers.Of(new Dictionary<string, int> { ["id"] = 1 }, typeof(Dictionary<string, int>), preSerialized: false));

            await AssertConnectionFailsAsync(host, HttpMethod.Post, "/act", committed.Task);

            Assert.True(committed.Task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task A_write_sent_before_its_timeout_whose_large_answer_comes_after_it_loses_its_connection()
        {
            TaskCompletionSource<bool> committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await StartAsync("/save", HttpMethod.Post, persists: true, committed, static () => Results.Ok(LargeReadBack()));

            await AssertConnectionFailsAsync(host, HttpMethod.Post, "/save", committed.Task);

            Assert.True(committed.Task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task A_read_whose_answer_began_after_its_timeout_loses_its_connection_rather_than_answering_a_500()
        {
            TaskCompletionSource<bool> answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await StartAsync("/read", HttpMethod.Get, persists: false, answered, static () => ActionAnswers.Of(LargeReadBack(), typeof(List<ReadBackRow>), preSerialized: false));

            await AssertConnectionFailsAsync(host, HttpMethod.Get, "/read", answered.Task);
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

        /// <summary>
        ///     Starts a host with one endpoint whose timeout fires while it runs and which answers only
        ///     after that, as a request whose round trip straddles its timeout does.
        /// </summary>
        /// <param name="path">The endpoint's path under the web surface.</param>
        /// <param name="method">The endpoint's method; a POST is a mutation.</param>
        /// <param name="persists">Whether the endpoint sends a persist before its timeout, which then runs to its end.</param>
        /// <param name="finished">Completed once the endpoint's work finished past the timeout.</param>
        /// <param name="answer">The endpoint's answer, written after its timeout fired.</param>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartAsync(string path, HttpMethod method, bool persists, TaskCompletionSource<bool> finished, Func<IResult> answer)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                options.Map = (app, endpoints) =>
                {
                    async Task<IResult> Handle(HttpContext context)
                    {
                        // A sent persist: it completes whatever cancels the request, as the executor's does.
                        if (persists)
                        {
                            context.RequestServices.GetRequiredService<DataAccessScope>().PersistSent = true;
                        }

                        // The timeout fires while the work runs, after the request passed the tenant front door.
                        FireTimeout(context);
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                        }
                        catch (OperationCanceledException)
                        {
                            // The work outlives the cancellation and returns its answer.
                        }

                        _ = finished.TrySetResult(true);
                        return answer();
                    }

                    // Cast to a route handler, whose answer is written to the response.
                    Delegate handler = Handle;
                    _ = (method == HttpMethod.Post ? endpoints.Web.MapPost(path, handler) : endpoints.Web.MapGet(path, handler))
                        .WithMutation(method == HttpMethod.Post)
                        .AllowMember();
                };
            });
        }

        /// <summary>
        ///     Asserts a request's connection fails once its endpoint finished past the timeout: no
        ///     status, neither a 504, a 500 nor a 200, reaches its client.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="method">The method.</param>
        /// <param name="path">The endpoint's path under the web surface.</param>
        /// <param name="finished">Completes once the endpoint's work finished past the timeout, before its answer is written.</param>
        /// <returns>A task that completes when the assertion holds.</returns>
        private static async Task AssertConnectionFailsAsync(TellmaTestHost host, HttpMethod method, string path, Task finished)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(method, "/1/api/web" + path, host.Cookie());
            Task<HttpResponseMessage> sent = host.Client.SendAsync(request, Token);

            // --- The endpoint ran and finished, however long the cold host took to reach it ---
            // Its answer is written only then, so what follows waits on that answer alone.
            await Patience.SignalledAsync(finished, sent);
            HttpResponseMessage? response = null;
            Exception? failure = await Record.ExceptionAsync(async () => response = await Patience.WaitAsync(sent));

            using (response)
            {
                Assert.True(response is null, $"The request answered {(int?)response?.StatusCode} rather than losing its connection.");
            }

            Assert.NotNull(failure);
            Assert.IsNotType<TimeoutException>(failure);
        }

        /// <summary>
        ///     Fires the request's own timeout now, through the source the framework's timeout stage
        ///     created for it, so that the timeout fires while the endpoint runs.
        /// </summary>
        /// <param name="context">The request, under the web policy's default timeout, which does not fire by itself during a test.</param>
        /// <exception cref="InvalidOperationException">The timeout stage did not run, or the framework no longer holds its source where this reads it.</exception>
        private static void FireTimeout(HttpContext context)
        {
            // A lowered timer starts at the timeout stage, ahead of the tenant front door and the
            // access guard, whose request-context initializers throw on a cancelled token. On a loaded
            // runner a timer of a few hundred milliseconds can fire there, before the endpoint
            // runs, and the request then answers 504, as a request with nothing written must, so a
            // test that needs its endpoint running when the timeout fires would never reach what it
            // pins. The framework offers no public way to fire a timeout early, so this cancels the
            // feature's own source. A framework that renames the field fails here, loudly, rather
            // than by timing.
            IHttpRequestTimeoutFeature feature = context.Features.Get<IHttpRequestTimeoutFeature>()
                ?? throw new InvalidOperationException("The request carries no timeout feature: the timeout stage did not run for it.");
            FieldInfo field = feature.GetType().GetField("_timeoutCancellationTokenSource", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"{feature.GetType().FullName} no longer holds its source in _timeoutCancellationTokenSource.");
            CancellationTokenSource source = field.GetValue(feature) as CancellationTokenSource
                ?? throw new InvalidOperationException($"{feature.GetType().FullName}._timeoutCancellationTokenSource holds no source.");
            source.Cancel();

            if (!feature.RequestTimeoutToken.IsCancellationRequested || !context.RequestAborted.IsCancellationRequested)
            {
                throw new InvalidOperationException("Cancelling the timeout source did not fire the request's timeout.");
            }
        }

        /// <summary>A read-back larger than the serializer's first flush.</summary>
        /// <returns>The rows.</returns>
        private static List<ReadBackRow> LargeReadBack()
        {
            return [.. Enumerable.Range(0, 5_000).Select(static id => new ReadBackRow(id, "a row of the read-back"))];
        }

        /// <summary>A row of a large read-back.</summary>
        /// <param name="Id">The row's id.</param>
        /// <param name="Name">The row's name.</param>
        public sealed record ReadBackRow(int Id, string Name);
    }
}
