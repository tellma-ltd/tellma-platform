// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     The named HTTP client that fetches client metadata documents and their key sets, and
    ///     the primary handler that holds its address guard.
    /// </summary>
    public static class ClientMetadataHttp
    {
        /// <summary>The name of the <c>IHttpClientFactory</c> client every metadata fetch goes through.</summary>
        public const string ClientName = "Tellma.Identity.ClientMetadata";

        /// <summary>The largest response header block read, in kilobytes; a document needs few headers.</summary>
        public const int MaxResponseHeadersKilobytes = 16;

        /// <summary>
        ///     Builds the named client's primary handler. It connects only through
        ///     <paramref name="connector" />, which resolves the host and checks every address
        ///     itself; it uses no proxy, follows no redirect, sends and keeps no cookie, and does
        ///     not decompress, so the body limit applies to the bytes the server sent.
        /// </summary>
        /// <param name="connector">The guarded connection step.</param>
        /// <param name="connectTimeout">The longest a connection attempt may take.</param>
        /// <returns>The handler.</returns>
        public static SocketsHttpHandler CreatePrimaryHandler(ClientMetadataConnector connector, TimeSpan connectTimeout)
        {
            ArgumentNullException.ThrowIfNull(connector);

            return new SocketsHttpHandler
            {
                // A proxy would resolve and connect on the server's behalf, past the address guard.
                UseProxy = false,
                Proxy = null,

                // A redirect could point anywhere; the fetch refuses one rather than following it.
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None,
                MaxResponseHeadersLength = MaxResponseHeadersKilobytes,
                ConnectTimeout = connectTimeout,

                // A pooled connection was made to a checked address and may be reused for the
                // same host; it is recycled soon so a long-lived one does not outlast the
                // resolution that checked it by much.
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                ConnectCallback = (context, cancellationToken) => connector.ConnectAsync(context.DnsEndPoint, cancellationToken),
            };
        }
    }
}
