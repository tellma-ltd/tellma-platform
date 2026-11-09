// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Security.Claims;
using Tellma.Identity.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     The shared step of every endpoint's resource check: ask the one validator, and turn a
    ///     refusal into <c>invalid_target</c>.
    /// </summary>
    internal static class ResourceGrantCheck
    {
        /// <summary>Evaluates a request's resources and rejects the request when one is refused.</summary>
        /// <param name="context">The endpoint's validation context.</param>
        /// <param name="validator">The resource grant validator.</param>
        /// <param name="request">The request's resources and context.</param>
        /// <returns>A task that completes when the decision has been applied.</returns>
        public static async ValueTask ApplyAsync(
            BaseValidatingContext context, ResourceGrantValidator validator, ResourceGrantRequest request)
        {
            if (await validator.FindRefusedAsync(request, context.CancellationToken) is not null)
            {
                context.Reject(error: Errors.InvalidTarget, description: ResourceGrantValidator.RefusedDescription);
            }
        }
    }

    /// <summary>
    ///     Checks the <c>resource</c> parameters of an authorization request — including one
    ///     restored from a pushed request — against the client's grants. It replaces OpenIddict's
    ///     own per-client resource-permission check, which the engine turns off so that one rule
    ///     decides every endpoint.
    /// </summary>
    /// <param name="validator">The resource grant validator.</param>
    public sealed class ValidateAuthorizationResourcesHandler(ResourceGrantValidator validator)
        : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
    {
        /// <summary>
        ///     The handler registration: in the slot of OpenIddict's resource-permission check, after
        ///     the client, its redirect URI and its scope permissions are validated, so a refusal is
        ///     returned to a verified callback.
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
                .UseScopedHandler<ValidateAuthorizationResourcesHandler>()
                .SetOrder(OpenIddictServerHandlers.Authentication.ValidateResourcePermissions.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ResourceGrantCheck.ApplyAsync(
                context,
                validator,
                new ResourceGrantRequest(
                    context.ClientId ?? string.Empty,
                    [.. context.Request.GetResources()],
                    [.. context.Request.GetScopes()],
                    GrantType: null,
                    StoredResources: null));
        }
    }

    /// <summary>
    ///     Checks the <c>resource</c> parameters of a pushed authorization request against the
    ///     client's grants, so a refused resource never gets a <c>request_uri</c>.
    /// </summary>
    /// <param name="validator">The resource grant validator.</param>
    public sealed class ValidatePushedAuthorizationResourcesHandler(ResourceGrantValidator validator)
        : IOpenIddictServerHandler<ValidatePushedAuthorizationRequestContext>
    {
        /// <summary>The handler registration: in the slot of OpenIddict's pushed resource-permission check.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidatePushedAuthorizationRequestContext>()
                .UseScopedHandler<ValidatePushedAuthorizationResourcesHandler>()
                .SetOrder(OpenIddictServerHandlers.Authentication.ValidatePushedResourcePermissions.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ValidatePushedAuthorizationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ResourceGrantCheck.ApplyAsync(
                context,
                validator,
                new ResourceGrantRequest(
                    context.ClientId ?? string.Empty,
                    [.. context.Request.GetResources()],
                    [.. context.Request.GetScopes()],
                    GrantType: null,
                    StoredResources: null));
        }
    }

    /// <summary>
    ///     Checks the <c>resource</c> parameters of a device authorization request against the
    ///     client's grants. OpenIddict has no resource check of its own at this endpoint.
    /// </summary>
    /// <param name="validator">The resource grant validator.</param>
    public sealed class ValidateDeviceAuthorizationResourcesHandler(ResourceGrantValidator validator)
        : IOpenIddictServerHandler<ValidateDeviceAuthorizationRequestContext>
    {
        /// <summary>The handler registration: after the client and its scope permissions are validated.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateDeviceAuthorizationRequestContext>()
                .UseScopedHandler<ValidateDeviceAuthorizationResourcesHandler>()
                .SetOrder(OpenIddictServerHandlers.Device.ValidateScopePermissions.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ValidateDeviceAuthorizationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ResourceGrantCheck.ApplyAsync(
                context,
                validator,
                new ResourceGrantRequest(
                    context.ClientId ?? string.Empty,
                    [.. context.Request.GetResources()],
                    [.. context.Request.GetScopes()],
                    GrantType: null,
                    StoredResources: null));
        }
    }

    /// <summary>
    ///     Carries a device authorization request's validated resources into the device and user
    ///     codes. OpenIddict's default device principal keeps only the scopes, so without this a
    ///     device grant could never name the audience it was approved for.
    /// </summary>
    public sealed class AttachDeviceAuthorizationResourcesHandler : IOpenIddictServerHandler<HandleDeviceAuthorizationRequestContext>
    {
        /// <summary>The handler registration: the endpoint's only handling step.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleDeviceAuthorizationRequestContext>()
                .UseSingletonHandler<AttachDeviceAuthorizationResourcesHandler>()
                .SetOrder(int.MinValue + 100_000)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(HandleDeviceAuthorizationRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Principal is not null)
            {
                return ValueTask.CompletedTask;
            }

            // The principal OpenIddict would build — an unauthenticated identity holding the
            // requested scopes — plus the resources the validation stage already accepted.
            ClaimsPrincipal principal = new(new ClaimsIdentity());
            principal.SetScopes(context.Request.GetScopes());
            principal.SetResources(context.Request.GetResources());
            context.SignIn(principal);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Checks the <c>resource</c> parameters of a token request. A request that presents a
    ///     stored grant — an authorization code, device code or refresh token — is evaluated
    ///     against that grant's own resources, so it may narrow the audience but never widen it.
    ///     Client credentials and token exchange are evaluated against the client's grants.
    /// </summary>
    /// <param name="validator">The resource grant validator.</param>
    public sealed class ValidateTokenResourcesHandler(ResourceGrantValidator validator)
        : IOpenIddictServerHandler<ValidateTokenRequestContext>
    {
        /// <summary>
        ///     The handler registration: in the slot of OpenIddict's resource-permission check, after
        ///     the client is authenticated and the presented grant is validated.
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenRequestContext>()
                .UseScopedHandler<ValidateTokenResourcesHandler>()
                .SetOrder(OpenIddictServerHandlers.Exchange.ValidateResourcePermissions.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ValidateTokenRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            OpenIddictRequest request = context.Request;
            StoredGrantPrincipal stored = StoredGrant(context);

            // A grant that should carry a stored principal but does not has nothing to narrow
            // from: everything it names is refused rather than checked against the client.
            IReadOnlyCollection<string>? storedResources = null;
            if (stored.IsStoredGrant)
            {
                storedResources = stored.Principal is { } principal ? [.. principal.GetResources()] : [];
            }

            return ResourceGrantCheck.ApplyAsync(
                context,
                validator,
                new ResourceGrantRequest(
                    context.ClientId ?? string.Empty,
                    [.. request.GetResources()],
                    [.. request.GetScopes()],
                    request.GrantType,
                    storedResources));
        }

        /// <summary>The stored grant a token request presents, when its grant type presents one.</summary>
        private static StoredGrantPrincipal StoredGrant(ValidateTokenRequestContext context)
        {
            OpenIddictRequest request = context.Request;
            return request.IsAuthorizationCodeGrantType() ? new(true, context.AuthorizationCodePrincipal)
                : request.IsDeviceCodeGrantType() ? new(true, context.DeviceCodePrincipal)
                : request.IsRefreshTokenGrantType() ? new(true, context.RefreshTokenPrincipal)
                : new(false, null);
        }

        /// <summary>Whether the grant type presents a stored grant, and its principal when it does.</summary>
        /// <param name="IsStoredGrant">The grant type redeems a stored grant.</param>
        /// <param name="Principal">The stored grant's principal, when resolved.</param>
        private readonly record struct StoredGrantPrincipal(bool IsStoredGrant, ClaimsPrincipal? Principal);
    }
}
