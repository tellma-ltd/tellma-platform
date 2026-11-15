// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Server;
using Tellma.Identity.Options;
using Tellma.Identity.Services;
using Tellma.Identity.Services.ClientMetadata;
using Tellma.Identity.Services.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     Resolves a document client before the built-in client validation on the authorization,
    ///     pushed-authorization, token and revocation endpoints, and converges its application row
    ///     on the document. From then on the request is an ordinary one for a registered client:
    ///     the row is what the built-in checks — client type, redirect URI, permissions, PKCE,
    ///     client assertion keys — read. A document that cannot be resolved refuses the request
    ///     with <c>invalid_client</c>, whatever the row holds.
    /// </summary>
    /// <param name="resolver">Resolves the document.</param>
    /// <param name="converger">Converges the row.</param>
    /// <param name="auditor">Records a refusal the converger decides.</param>
    /// <param name="timeProvider">The clock a new row's creation time is read from.</param>
    public sealed class DocumentClientHandler(
        IClientMetadataDocumentResolver resolver,
        ClientRegistrationConverger converger,
        ClientMetadataAuditor auditor,
        TimeProvider timeProvider) : IOpenIddictServerHandler<ProcessAuthenticationContext>
    {
        /// <summary>The description of a refused document client, whatever the reason; the audit row carries the reason.</summary>
        public const string RefusedDescription = "The client's metadata document cannot be used.";

        /// <summary>
        ///     The handler registration: after the request's tokens are extracted and before the
        ///     client assertion is validated, so a <c>private_key_jwt</c> client's key set is on its
        ///     row when the assertion's signature is checked, and the row exists when the client id
        ///     is looked up.
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .UseScopedHandler<DocumentClientHandler>()
                .SetOrder(OpenIddictServerHandlers.ValidateRequiredTokens.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public async ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.EndpointType is not (OpenIddictServerEndpointType.Authorization
                or OpenIddictServerEndpointType.PushedAuthorization
                or OpenIddictServerEndpointType.Token
                or OpenIddictServerEndpointType.Revocation))
            {
                return;
            }

            // The client id is the request's, or — for a client that authenticates with an
            // assertion and names itself only there — the assertion's issuer, read unvalidated:
            // it decides only which document to fetch, and the assertion is validated against
            // that document's keys right after.
            string? clientId = context.Request?.ClientId;
            if (string.IsNullOrEmpty(clientId)
                && string.Equals(context.Request?.ClientAssertionType, ClientAssertionTypes.JwtBearer, StringComparison.Ordinal))
            {
                clientId = PeekIssuer(context.Request?.ClientAssertion);
            }

            if (!ClientIdUrl.TryRecognize(clientId, out Uri? url))
            {
                return;
            }

            ClientMetadataDocument? document;
            try
            {
                document = await resolver.ResolveAsync(url, context.CancellationToken);

                // A request naming a redirect URI the cached copy does not list may be for a
                // changed document: fetch it again, ahead of its expiry, before the built-in
                // redirect check refuses it. A request restored from a pushed one names its
                // redirect URI only after this point; it was checked when it was pushed.
                if (document is not null
                    && context.EndpointType is OpenIddictServerEndpointType.Authorization or OpenIddictServerEndpointType.PushedAuthorization
                    && context.Request?.RedirectUri is { Length: > 0 } redirectUri
                    && !DocumentClientRedirects.Matches(document.RedirectUris, redirectUri))
                {
                    document = await resolver.RevalidateAsync(url, context.CancellationToken) ?? document;
                }
            }
            catch (ClientMetadataException)
            {
                // The resolver has audited the reason.
                Refuse(context);
                return;
            }

            if (document is null)
            {
                return;
            }

            DocumentClientConvergence outcome = await converger.ConvergeDocumentClientAsync(
                document, timeProvider.GetUtcNow(), context.CancellationToken);
            string? reason = outcome switch
            {
                DocumentClientConvergence.Cap => ClientMetadataRejectionReasons.Cap,
                DocumentClientConvergence.Registered => ClientMetadataRejectionReasons.Registered,
                DocumentClientConvergence.Created or DocumentClientConvergence.Updated or DocumentClientConvergence.Unchanged => null,
                _ => throw new InvalidOperationException($"Unknown document-client convergence outcome {outcome}."),
            };
            if (reason is not null)
            {
                await auditor.RejectedAsync(clientId, reason, url.IdnHost, AuditContext.ClientIpAddress(context.Transaction));
                Refuse(context);
            }
        }

        /// <summary>Refuses the request as from an unusable client.</summary>
        private static void Refuse(ProcessAuthenticationContext context)
        {
            context.Reject(error: Errors.InvalidClient, description: RefusedDescription);
        }

        /// <summary>The issuer of an unvalidated JWT, or null when it is not one.</summary>
        /// <param name="assertion">The client assertion.</param>
        /// <returns>The <c>iss</c> claim.</returns>
        internal static string? PeekIssuer(string? assertion)
        {
            if (string.IsNullOrEmpty(assertion))
            {
                return null;
            }

            try
            {
                return new JsonWebTokenHandler().ReadJsonWebToken(assertion).Issuer;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    ///     Advertises document clients in the discovery document while they are enabled:
    ///     <c>client_id_metadata_document_supported</c>, and <c>none</c> among the token
    ///     endpoint's authentication methods, which is what a hosted agent looks for before it
    ///     takes the document path.
    /// </summary>
    /// <param name="options">The engine options.</param>
    public sealed class AdvertiseDocumentClientsHandler(IOptions<TellmaIdentityOptions> options)
        : IOpenIddictServerHandler<HandleConfigurationRequestContext>
    {
        /// <summary>The discovery metadata member that announces support.</summary>
        public const string SupportedMetadata = "client_id_metadata_document_supported";

        /// <summary>The handler registration: after OpenIddict lists the authentication methods.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
                .UseSingletonHandler<AdvertiseDocumentClientsHandler>()
                .SetOrder(OpenIddictServerHandlers.Discovery.AttachClientAuthenticationMethods.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(HandleConfigurationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (options.Value.ClientMetadata.Enabled)
            {
                context.Metadata[SupportedMetadata] = true;
                context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
            }

            return ValueTask.CompletedTask;
        }
    }
}
