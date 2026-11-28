// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Testing.WebHost;

namespace Tellma.Core.Mcp.Tests.Hosting
{
    /// <summary>
    ///     A front door the request's timeout outruns, under a one-second tool timeout: whether the
    ///     door never decides, or decides only after the timeout fired because a wait inside it did
    ///     not end with the request's cancellation, the initialize is answered 504 timeout and never
    ///     left without a reply.
    /// </summary>
    public sealed class SlowDoorTests : IAsyncLifetime
    {
        /// <summary>The subject whose door waits until the request is cancelled.</summary>
        private const string Never = "sub-never";

        /// <summary>The subject whose door ignores the cancellation, then refuses the caller.</summary>
        private const string LateRefusal = "sub-late-refusal";

        /// <summary>The subject whose door ignores the cancellation, then admits the caller.</summary>
        private const string LateAdmission = "sub-late-admission";

        /// <summary>How long a late door takes: past the one-second timeout.</summary>
        private static readonly TimeSpan LateBy = TimeSpan.FromSeconds(3);

        /// <summary>The host; null until started.</summary>
        private TellmaTestHost? _host;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData(Never)]
        [InlineData(LateRefusal)]
        [InlineData(LateAdmission)]
        public async Task An_initialize_whose_door_the_timeout_outran_is_answered_504_timeout(string subject)
        {
            // --- An initialize, posted as the SDK's client posts it ---
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Post, "/1/mcp", McpHost.Token(McpHost.Resource(1), subject: subject));
            request.Content = new StringContent(
                /*lang=json,strict*/ """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0"}}}""",
                Encoding.UTF8,
                "application/json");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");

            // A request left unanswered is aborted, which the send throws.
            using HttpResponseMessage response = await _host!.Client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Contains("\"code\":\"timeout\"", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _host = await TellmaTestHost.StartAsync(static options =>
            {
                // --- The server under a one-second timeout, and a door step after the user connection ---
                options.MapProbes = false;
                options.ComposeWeb = static tellma =>
                {
                    _ = tellma.AddMcp(static mcp => mcp.ToolTimeout = TimeSpan.FromSeconds(1));
                    _ = tellma.Services.AddScoped<IRequestContextInitializer, SlowInitializer>();
                };
            });
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }

        /// <summary>A door step that, for the slow subjects, outlasts the request's timeout.</summary>
        private sealed class SlowInitializer : IRequestContextInitializer
        {
            /// <inheritdoc />
            public int Order => 101;

            /// <inheritdoc />
            public async Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(context);
                switch (context.Subject)
                {
                    case Never:
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        break;

                    // A wait that does not end with the token, as a pool wait or a command's own timeout does not.
                    case LateRefusal:
                        await Task.Delay(LateBy, CancellationToken.None);
                        throw new TenantNotFoundException(context.RequireTenant().Id);

                    case LateAdmission:
                        await Task.Delay(LateBy, CancellationToken.None);
                        break;

                    default:
                        break;
                }

                return context;
            }
        }
    }
}
