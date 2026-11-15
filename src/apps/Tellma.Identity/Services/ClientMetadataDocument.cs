// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.Tokens;

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     A document client's validated metadata: what the server fetched from the URL that is
    ///     the client's id, and for how long that answer stands.
    /// </summary>
    /// <param name="ClientId">The client id, the URL the document was fetched from; its original string is the id exactly.</param>
    /// <param name="ClientName">The document's <c>client_name</c>, when it has one.</param>
    /// <param name="RedirectUris">The document's <c>redirect_uris</c>, each absolute, as written in the document.</param>
    /// <param name="TokenEndpointAuthMethod">The document's <c>token_endpoint_auth_method</c>: <c>none</c> or <c>private_key_jwt</c>.</param>
    /// <param name="JwksUri">The document's <c>jwks_uri</c>, on the document's own origin; null for <c>none</c>.</param>
    /// <param name="Keys">The key set fetched from <paramref name="JwksUri" />; null for <c>none</c>.</param>
    /// <param name="FetchedAt">When the document was fetched.</param>
    /// <param name="ExpiresAt">
    ///     Until when it stands: its HTTP cache lifetime (and its key set's) clamped to the
    ///     configured window. After that it is fetched again, and refused if that fails.
    /// </param>
    public sealed record ClientMetadataDocument(
        Uri ClientId,
        string? ClientName,
        IReadOnlyList<Uri> RedirectUris,
        string TokenEndpointAuthMethod,
        Uri? JwksUri,
        JsonWebKeySet? Keys,
        DateTimeOffset FetchedAt,
        DateTimeOffset ExpiresAt);
}
