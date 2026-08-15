// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Webhooks.Tests.Infrastructure
{
    /// <summary>
    ///     A real in-memory web host around <c>MapTellmaWebhooks</c>, so the fronting is exercised
    ///     through the pipeline a deployment actually runs rather than by calling the handler.
    /// </summary>
    public sealed class WebhookTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private WebhookTestHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        /// <summary>A client bound to the in-memory server.</summary>
        public HttpClient Client { get; }

        /// <summary>The built container.</summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>Starts a host with the supplied receivers.</summary>
        /// <param name="receivers">The receivers to register.</param>
        /// <param name="settings">Configuration entries, e.g. the body cap.</param>
        /// <returns>The running host.</returns>
        public static async Task<WebhookTestHost> StartAsync(
            IEnumerable<IWebhookReceiver> receivers, IDictionary<string, string?>? settings = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

            builder.Services.AddTellmaWebhooks();
            foreach (IWebhookReceiver receiver in receivers)
            {
                builder.Services.AddSingleton(receiver);
            }

            WebApplication app = builder.Build();
            app.MapTellmaWebhooks();
            await app.StartAsync();

            return new WebhookTestHost(app);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
