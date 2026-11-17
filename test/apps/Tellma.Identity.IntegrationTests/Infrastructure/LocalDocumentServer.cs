// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Publishes client ID metadata documents and key sets from memory: it becomes the primary
    ///     handler of the engine's metadata client, so the host fetches every document from here
    ///     and no test reaches a real host. Replacing the primary handler also replaces the
    ///     address guard, which the unit suite covers on its own.
    /// </summary>
    public sealed class LocalDocumentServer : HttpMessageHandler
    {
        /// <summary>The published bodies, by absolute URL.</summary>
        private readonly ConcurrentDictionary<string, (string Body, string ContentType)> _published = new(StringComparer.Ordinal);

        /// <summary>The URLs that answer as an unreachable host does.</summary>
        private readonly ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);

        /// <summary>Every URL fetched, in order.</summary>
        public ConcurrentQueue<Uri> Fetched { get; } = new();

        /// <summary>Routes a host's metadata client through this server.</summary>
        /// <param name="factory">The host, before it starts.</param>
        public void Attach(StandaloneFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            factory.ServiceOverrides.Add(services => services
                .AddHttpClient(ClientMetadataHttp.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => this));
        }

        /// <summary>Publishes a body at a URL, replacing what was there, and makes the URL answer again.</summary>
        /// <param name="url">The URL.</param>
        /// <param name="body">The JSON body.</param>
        /// <param name="contentType">Its media type.</param>
        public void Publish(string url, string body, string contentType = "application/json")
        {
            _published[url] = (body, contentType);
            _failing.TryRemove(url, out _);
        }

        /// <summary>Makes a URL fail as an unreachable host does.</summary>
        /// <param name="url">The URL.</param>
        public void Fail(string url)
        {
            _failing[url] = true;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri url = request.RequestUri!;
            Fetched.Enqueue(url);

            // A failing URL answers as a host that cannot be reached; anything unpublished is a 404.
            return _failing.ContainsKey(url.AbsoluteUri)
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("The host is unreachable."))
                : Task.FromResult(_published.TryGetValue(url.AbsoluteUri, out (string Body, string ContentType) published)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(published.Body, Encoding.UTF8, published.ContentType) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
