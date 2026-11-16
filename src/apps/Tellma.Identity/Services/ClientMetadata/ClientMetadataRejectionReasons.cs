// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     The closed set of reasons a document client is refused, recorded on every
    ///     <c>ClientMetadataRejected</c> audit row. Dashboards key on these values.
    /// </summary>
    public static class ClientMetadataRejectionReasons
    {
        /// <summary>Document clients are turned off in this deployment.</summary>
        public const string Disabled = "disabled";

        /// <summary>The client id is longer than the store's client id column.</summary>
        public const string ClientIdTooLong = "client_id_length";

        /// <summary>
        ///     The client id is not in canonical form, holds user information or dot segments, or
        ///     is not plain ASCII.
        /// </summary>
        public const string ClientId = "client_id";

        /// <summary>
        ///     The address guard: not <c>https</c>, an IP literal, or a host that resolves to a
        ///     loopback, link-local, private, multicast or otherwise non-public address.
        /// </summary>
        public const string Address = "address";

        /// <summary>The fetch was answered with a redirect, which is never followed.</summary>
        public const string Redirect = "redirect";

        /// <summary>The fetch was answered with a status other than 200.</summary>
        public const string Status = "status";

        /// <summary>The response's content type is not one the fetch accepts.</summary>
        public const string ContentType = "content_type";

        /// <summary>The response body is larger than the configured limit.</summary>
        public const string Size = "size";

        /// <summary>The fetch did not complete within the configured timeout.</summary>
        public const string Timeout = "timeout";

        /// <summary>The host was not reachable: its name did not resolve, or the connection or handshake failed.</summary>
        public const string Unreachable = "unreachable";

        /// <summary>The host has been sent its quota of fetches for the current minute.</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>The body is not a JSON object, or repeats a member.</summary>
        public const string Document = "document";

        /// <summary>The document's <c>client_id</c> is not exactly the URL it was fetched from.</summary>
        public const string ClientIdMismatch = "client_id_mismatch";

        /// <summary>The document's <c>redirect_uris</c> are missing, empty, or hold an unacceptable entry.</summary>
        public const string RedirectUris = "redirect_uris";

        /// <summary>The document's <c>token_endpoint_auth_method</c> is missing or neither <c>none</c> nor <c>private_key_jwt</c>.</summary>
        public const string AuthMethod = "token_endpoint_auth_method";

        /// <summary>The document carries an inline <c>jwks</c>, which is refused.</summary>
        public const string InlineJwks = "jwks";

        /// <summary>
        ///     The document's <c>jwks_uri</c> is missing for <c>private_key_jwt</c>, present for
        ///     <c>none</c>, not a URL, or not on the document's own origin.
        /// </summary>
        public const string JwksUri = "jwks_uri";

        /// <summary>The document's <c>grant_types</c> name a grant other than <c>authorization_code</c> and <c>refresh_token</c>.</summary>
        public const string GrantTypes = "grant_types";

        /// <summary>The document's <c>client_name</c> is not a short, printable string.</summary>
        public const string ClientName = "client_name";

        /// <summary>The key set behind <c>jwks_uri</c> is malformed, holds an unacceptable key, or no signing key.</summary>
        public const string KeySet = "key_set";

        /// <summary>The store holds its maximum number of document clients, so a new one is refused.</summary>
        public const string Cap = "cap";

        /// <summary>A registered client already holds this client id, so no document may take its row over.</summary>
        public const string Registered = "registered";

        /// <summary>A stored client id differs from this one only in letter case.</summary>
        public const string ClientIdCollision = "client_id_collision";
    }
}
