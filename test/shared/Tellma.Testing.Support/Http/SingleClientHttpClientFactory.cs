// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Testing.Support.Http
{
    /// <summary>An <see cref="IHttpClientFactory" /> that hands out one client, whatever the name.</summary>
    /// <param name="handler">The handler every client sends through.</param>
    public sealed class SingleClientHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];

        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            // A fresh client per call, matching the factory's own semantics, over the one handler
            // whose script the test controls.
            HttpClient client = new(handler, disposeHandler: false);
            _clients.Add(client);
            return client;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (HttpClient client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
            handler.Dispose();
        }
    }
}
