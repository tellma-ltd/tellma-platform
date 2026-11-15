// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>What a metadata fetch retrieves, which decides the content types it accepts.</summary>
    public enum ClientMetadataResource
    {
        /// <summary>A client ID metadata document: <c>application/json</c>.</summary>
        Document,

        /// <summary>A <c>private_key_jwt</c> client's key set: <c>application/json</c> or <c>application/jwk-set+json</c>.</summary>
        KeySet,
    }

    /// <summary>A response body that passed the fetch guards.</summary>
    /// <param name="Body">The body's bytes, at most the configured limit.</param>
    /// <param name="FetchedAt">When the response's headers arrived.</param>
    /// <param name="Lifetime">The freshness lifetime its caching headers give it, before clamping.</param>
    public sealed record ClientMetadataFetchResult(byte[] Body, DateTimeOffset FetchedAt, TimeSpan Lifetime);

    /// <summary>
    ///     Fetches a metadata document or key set under the response and limit guards: an
    ///     <c>https</c> URL with a host name, the per-host rate limit, the per-fetch timeout, a 200
    ///     answer (a redirect is refused, never followed), an accepted content type and a body
    ///     within the configured size. The address guard is the named client's own connection
    ///     step, so every fetch goes through that client.
    /// </summary>
    /// <param name="httpClientFactory">Creates the named metadata client.</param>
    /// <param name="rateLimiter">The per-host fetch limit.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="timeProvider">The clock the timeout runs on.</param>
    public sealed class ClientMetadataFetcher(
        IHttpClientFactory httpClientFactory,
        ClientMetadataRateLimiter rateLimiter,
        IOptions<TellmaIdentityOptions> options,
        TimeProvider timeProvider)
    {
        /// <summary>The media type every fetch accepts.</summary>
        public const string JsonMediaType = "application/json";

        /// <summary>The key-set media type (RFC 7517, section 8.5) a key-set fetch accepts besides JSON.</summary>
        public const string JwkSetMediaType = "application/jwk-set+json";

        /// <summary>Fetches a URL.</summary>
        /// <param name="url">The document or key-set URL.</param>
        /// <param name="resource">What is fetched.</param>
        /// <param name="cancellationToken">Aborts the fetch.</param>
        /// <returns>The guarded body.</returns>
        /// <exception cref="ClientMetadataException">A guard refused the fetch or its response, or the fetch failed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        public async Task<ClientMetadataFetchResult> FetchAsync(Uri url, ClientMetadataResource resource, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);

            TellmaIdentityClientMetadataOptions settings = options.Value.ClientMetadata;

            // The address guard's first half, on the URL: https, a host name, never an IP literal.
            // The second half resolves the name and checks its addresses at connection time.
            if (!url.IsAbsoluteUri
                || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || url.HostNameType is not (UriHostNameType.Dns or UriHostNameType.Basic)
                || string.IsNullOrEmpty(url.Host))
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.Address,
                    $"The metadata URL '{url}' is not an https URL with a host name.");
            }

            if (!rateLimiter.TryAcquire(url.IdnHost))
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.RateLimited,
                    $"The fetch limit for the host '{url.IdnHost}' is spent.");
            }

            // The timeout covers the whole fetch, connection and body included; the caller's own
            // token is linked so either ends it, and which one fired decides the outcome.
            using CancellationTokenSource timeout = new(settings.Timeout, timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                return await SendAsync(url, resource, settings.MaxBytes, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Our timeout, or the handler's connect timeout, which surfaces the same way.
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.Timeout,
                    $"The fetch of '{url}' timed out.");
            }
            catch (HttpRequestException exception)
            {
                // The connection step's refusal arrives wrapped in the handler's exception.
                bool refused = FindAddressRefusal(exception) is not null;
                throw new ClientMetadataException(
                    refused ? ClientMetadataRejectionReasons.Address : ClientMetadataRejectionReasons.Unreachable,
                    refused ? $"The host of '{url}' resolves to a refused address." : $"The fetch of '{url}' failed.",
                    exception);
            }
            catch (IOException exception)
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.Unreachable,
                    $"The response from '{url}' could not be read.",
                    exception);
            }
        }

        /// <summary>Sends the request and applies the response guards.</summary>
        private async Task<ClientMetadataFetchResult> SendAsync(Uri url, ClientMetadataResource resource, int maxBytes, CancellationToken cancellationToken)
        {
            HttpClient client = httpClientFactory.CreateClient(ClientMetadataHttp.ClientName);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
            if (resource is ClientMetadataResource.KeySet)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JwkSetMediaType));
            }

            // Headers first, so a refused status, type or declared length never reads the body.
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();

            // Status: only 200. A redirect has its own reason, since following one is what the
            // guard exists to prevent.
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.Redirect,
                    $"The fetch of '{url}' was answered with the redirect {status}, which is not followed.");
            }

            if (response.StatusCode is not HttpStatusCode.OK)
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.Status,
                    $"The fetch of '{url}' was answered with the status {status}.");
            }

            // Content type: the media type alone decides; parameters such as charset are ignored.
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            bool accepted = string.Equals(mediaType, JsonMediaType, StringComparison.OrdinalIgnoreCase)
                || (resource is ClientMetadataResource.KeySet
                    && string.Equals(mediaType, JwkSetMediaType, StringComparison.OrdinalIgnoreCase));
            if (!accepted)
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.ContentType,
                    $"The response from '{url}' has the content type '{mediaType}', which is not accepted.");
            }

            // Size: a declared length over the limit is refused before reading; the body is then
            // read to at most one byte past the limit, which a server that lies or streams
            // without a length cannot get past.
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw SizeRefusal(url, maxBytes);
            }

            byte[] body = await ReadBoundedAsync(response.Content, maxBytes, cancellationToken)
                ?? throw SizeRefusal(url, maxBytes);

            TimeSpan lifetime = ClientMetadataCacheLifetime.FromHeaders(response.Headers, response.Content.Headers, fetchedAt);
            return new ClientMetadataFetchResult(body, fetchedAt, lifetime);
        }

        /// <summary>Reads a body of at most <paramref name="maxBytes" /> bytes.</summary>
        /// <returns>The body, or null when it is longer.</returns>
        private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
        {
            await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);

            // One byte of headroom tells "exactly the limit" from "over it".
            byte[] buffer = new byte[maxBytes + 1];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
                if (read == 0)
                {
                    return buffer.AsSpan(0, total).ToArray();
                }

                total += read;
            }

            return null;
        }

        /// <summary>The refusal of a body over the limit.</summary>
        private static ClientMetadataException SizeRefusal(Uri url, int maxBytes)
        {
            return new ClientMetadataException(
                ClientMetadataRejectionReasons.Size,
                $"The response from '{url}' is larger than {maxBytes} bytes.");
        }

        /// <summary>The address refusal inside a handler exception, if that is what it wraps.</summary>
        private static ClientMetadataAddressException? FindAddressRefusal(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is ClientMetadataAddressException refusal)
                {
                    return refusal;
                }
            }

            return null;
        }
    }
}
