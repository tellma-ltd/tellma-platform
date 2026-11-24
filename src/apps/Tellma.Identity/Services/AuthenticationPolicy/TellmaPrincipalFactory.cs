// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Services.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>The outcome of building a protocol principal.</summary>
    /// <param name="Identity">The identity to sign in, when successful.</param>
    /// <param name="Error">The protocol error, when not.</param>
    /// <param name="ErrorDescription">The human-readable error description.</param>
    public sealed record PrincipalResult(ClaimsIdentity? Identity, string? Error, string? ErrorDescription);

    /// <summary>
    ///     The grant parameters the principal builder needs, decoupled from where they came from
    ///     (an authorization request or a stored device-code principal).
    /// </summary>
    /// <param name="ClientId">The requesting client.</param>
    /// <param name="Scopes">The granted scopes.</param>
    /// <param name="Resources">The explicitly requested resources, when any.</param>
    /// <param name="AllowedMethodsRaw">The tenant's method allow-list snapshot, when carried.</param>
    public sealed record GrantRequest(
        string ClientId,
        IReadOnlyCollection<string> Scopes,
        IReadOnlyCollection<string> Resources,
        string? AllowedMethodsRaw);

    /// <summary>
    ///     Builds the <see cref="ClaimsIdentity" /> every interactive grant signs in with — the
    ///     single place claims, assurance, audiences, the backing authorization, and session
    ///     registration are assembled, shared by the authorize and device-verification paths so
    ///     they cannot drift.
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="authorizationManager">The OpenIddict authorization manager.</param>
    /// <param name="scopeManager">The OpenIddict scope manager.</param>
    /// <param name="policyService">Assurance derivation.</param>
    /// <param name="sessionRegistry">The sid registry.</param>
    /// <param name="timeProvider">The clock a new authorization is dated by.</param>
    /// <param name="dbContext">The request's store context, the one the OpenIddict managers read through.</param>
    /// <param name="logger">Records grants that lose their authorization write to a concurrent grant.</param>
    public sealed class TellmaPrincipalFactory(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        IAuthenticationPolicyService policyService,
        ISessionRegistry sessionRegistry,
        TimeProvider timeProvider,
        TellmaIdentityDbContext dbContext,
        ILogger<TellmaPrincipalFactory> logger)
    {
        /// <summary>
        ///     How many times a grant writes the authorization it extends before it gives up on
        ///     concurrent grants. Each lost write means another grant's write landed, so a grant
        ///     gets through two others extending the same authorization at once.
        /// </summary>
        internal const int MaxAuthorizationWriteAttempts = 3;

        /// <summary>Builds the principal for an interactive authorization request.</summary>
        /// <param name="user">The authenticated user.</param>
        /// <param name="cookiePrincipal">The SSO-cookie principal carrying the session evidence.</param>
        /// <param name="request">The (PAR-resolved) protocol request.</param>
        /// <param name="application">The requesting client's application object.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The identity, or a protocol error.</returns>
        public Task<PrincipalResult> CreateAsync(
            TellmaIdentityUser user,
            ClaimsPrincipal cookiePrincipal,
            OpenIddictRequest request,
            object application,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            return CreateAsync(
                user,
                cookiePrincipal,
                new GrantRequest(
                    request.ClientId!,
                    [.. request.GetScopes()],
                    [.. request.GetResources()],
                    (string?)request[TellmaParameters.AllowedMethods]),
                application,
                cancellationToken);
        }

        /// <summary>Builds the principal for a grant whose parameters come from a stored principal.</summary>
        /// <param name="user">The authenticated user.</param>
        /// <param name="cookiePrincipal">The SSO-cookie principal carrying the session evidence.</param>
        /// <param name="grant">The grant's client, scopes, resources, and allow-list.</param>
        /// <param name="application">The requesting client's application object.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The identity, or a protocol error.</returns>
        public async Task<PrincipalResult> CreateAsync(
            TellmaIdentityUser user,
            ClaimsPrincipal cookiePrincipal,
            GrantRequest grant,
            object application,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(cookiePrincipal);
            ArgumentNullException.ThrowIfNull(grant);
            ArgumentNullException.ThrowIfNull(application);

            AssuranceResult? assurance = policyService.ReadAssurance(cookiePrincipal);
            string? sid = cookiePrincipal.FindFirst(TellmaClaims.Sid)?.Value;
            if (assurance is null || sid is null)
            {
                // A session cookie predating the engine's evidence model cannot mint tokens.
                return new PrincipalResult(null, Errors.LoginRequired, "The session must be re-established.");
            }

            // The tokens carry only the assurance the allow-list permits — the same filter the
            // authorization evaluation applied — so a resource server enforcing methods by
            // set-membership never sees a disallowed method in `tellma_methods`. An unparseable
            // list is refused rather than skipped: the caller pre-validates it, so reaching here
            // means the two disagree, and failing open would mint unfiltered tokens.
            if (!policyService.TryParseAllowedMethods(grant.AllowedMethodsRaw, out IReadOnlyList<string>? allowedMethods))
            {
                return new PrincipalResult(
                    null, Errors.InvalidRequest, "The tellma_allowed_methods parameter contains an unknown method.");
            }

            assurance = policyService.FilterAssurance(assurance, allowedMethods);
            if (assurance is null)
            {
                return new PrincipalResult(
                    null, Errors.LoginRequired, "The session must be re-established with a permitted method.");
            }

            ClaimsIdentity identity = new(
                TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

            identity.SetClaim(Claims.Subject, user.Id)
                    .SetClaim(Claims.Email, user.Email)
                    .SetClaim(Claims.Name, user.DisplayName)
                    .SetClaim(Claims.PreferredUsername, user.Email)
                    .SetClaim(Claims.Locale, user.Locale)
                    // Absent rather than empty when unstated: a relying party must be able to
                    // tell "no preference" from a value, so it can fall back to neutral wording.
                    .SetClaim(Claims.Gender, user.Gender?.ToString().ToLowerInvariant())
                    .SetClaim(Claims.AuthenticationContextReference, assurance.Acr)
                    .SetClaim(Claims.AuthenticationTime, assurance.AuthTime)

                    // The tier's own freshness travels beside it. `auth_time` keeps its standard
                    // meaning — the session's most recent authentication — because that is what
                    // relying parties and their libraries assume, and re-dating it would make the
                    // server assert a staleness it does not itself enforce. A relying party
                    // deciding whether a sensitive operation needs a step-up reads this instead,
                    // which is the only value that answers the question it is actually asking.
                    .SetClaim(TellmaClaims.AcrAuthTime, policyService.TierAuthTime(assurance))
                    .SetClaim(TellmaClaims.Sid, sid);
            identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);
            identity.SetClaims(Claims.AuthenticationMethodReference, [.. assurance.Amr]);
            identity.SetClaims(TellmaClaims.Methods, [.. assurance.Methods]);

            // The allow-list snapshot (server-side only) lets the refresh path re-enforce the
            // tenant's policy without a database read.
            if (!string.IsNullOrWhiteSpace(grant.AllowedMethodsRaw))
            {
                identity.SetClaim(TellmaClaims.AllowedMethods, grant.AllowedMethodsRaw);
            }

            // The security stamp (server-side only) lets refresh detect sign-out-everywhere.
            string? securityStamp = cookiePrincipal.FindFirst(TellmaClaimDestinations.SecurityStampClaimType)?.Value;
            if (securityStamp is not null)
            {
                identity.SetClaim(TellmaClaimDestinations.SecurityStampClaimType, securityStamp);
            }

            // The passkey signals (server-side only) ride the encrypted grant principal so the
            // refresh path re-derives the aal3 tier — and measures its freshness against the
            // assertion that carries it — instead of silently downgrading a hardware-key session
            // to aal2 or dating it from a weaker later factor.
            foreach (string claimType in (string[])[SignInClaims.PasskeyDeviceBound, SignInClaims.PasskeyAuthTime])
            {
                if (cookiePrincipal.FindFirst(claimType)?.Value is { } value)
                {
                    identity.SetClaim(claimType, value);
                }
            }

            identity.SetScopes(grant.Scopes);

            string? audienceError = await SetAudiencesAsync(identity, grant, application, cancellationToken);
            if (audienceError is not null)
            {
                return new PrincipalResult(null, Errors.InvalidTarget, audienceError);
            }

            // Attach (or create) the permanent authorization backing this grant; it anchors
            // consent memory and lets global logout revoke the client's refresh tokens. It records
            // every resource the user has granted the client, so consent remembered by it covers
            // those places and no other. An existing authorization that lacks one of this grant's
            // resources gains it (the user was asked first when the client's consent is explicit)
            // rather than a second one being created beside it, so consent stays remembered in one
            // place. The session registers every authorization a client's grants use under it, and
            // a logout revokes them all.
            string clientObjectId = (await applicationManager.GetIdAsync(application, cancellationToken))!;
            object? authorization = await AttachAuthorizationAsync(identity, user.Id, clientObjectId, cancellationToken);
            if (authorization is null)
            {
                return new PrincipalResult(
                    null, Errors.TemporarilyUnavailable, "The authorization changed while it was being granted; try again.");
            }

            string authorizationId = (await authorizationManager.GetIdAsync(authorization, cancellationToken))!;
            identity.SetAuthorizationId(authorizationId);

            await sessionRegistry.RegisterClientAsync(sid, grant.ClientId, authorizationId, cancellationToken);

            identity.SetDestinations(TellmaClaimDestinations.Resolve);
            return new PrincipalResult(identity, null, null);
        }

        /// <summary>
        ///     Whether consent is remembered for a grant: a valid permanent authorization of the
        ///     user's for the client holds the requested scopes and covers every requested resource.
        ///     An authorization the user granted for one distribution, or for one tenant of it,
        ///     therefore never answers for another, and the user is asked again.
        /// </summary>
        /// <param name="subject">The user's subject.</param>
        /// <param name="application">The requesting client's application object.</param>
        /// <param name="scopes">The requested scopes.</param>
        /// <param name="resources">The requested resources.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>True when a remembered grant covers the request.</returns>
        public async Task<bool> HasCoveringAuthorizationAsync(
            string subject,
            object application,
            IReadOnlyCollection<string> scopes,
            IReadOnlyCollection<string> resources,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(subject);
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(resources);

            string clientObjectId = (await applicationManager.GetIdAsync(application, cancellationToken))!;
            (object? covering, _) = await FindAuthorizationAsync(
                subject, clientObjectId, [.. scopes], resources, cancellationToken);
            return covering is not null;
        }

        /// <summary>
        ///     Finds the permanent authorization that backs a grant: one already covering its
        ///     resources, or else the user's existing one for the client, extended by them, or else
        ///     a new one. Grants of one user to one client may run at once (two tabs signing in), and
        ///     both may extend the same authorization from the same copy. The store refuses the
        ///     write that comes second, and that grant reads the authorizations again and goes on
        ///     from what the first wrote: usually it now covers the grant, and otherwise the grant
        ///     extends the authorization as it now stands. A grant that loses its write
        ///     <see cref="MaxAuthorizationWriteAttempts" /> times gives up. Two first grants at once
        ///     both find none and each create one, as the store has nothing to refuse; both then
        ///     succeed, and the session registers both, so a logout revokes both.
        /// </summary>
        /// <returns>The authorization, or null when the grant gave up.</returns>
        private async Task<object?> AttachAuthorizationAsync(
            ClaimsIdentity identity, string subject, string clientObjectId, CancellationToken cancellationToken)
        {
            ImmutableArray<string> resources = identity.GetResources();
            for (int attempt = 1; ; attempt++)
            {
                (object? covering, object? existing) = await FindAuthorizationAsync(
                    subject, clientObjectId, identity.GetScopes(), resources, cancellationToken);
                if (covering is not null)
                {
                    return covering;
                }

                if (existing is null)
                {
                    return await CreateAuthorizationAsync(identity, subject, clientObjectId, resources, cancellationToken);
                }

                if (await TryAddResourcesAsync(existing, resources, cancellationToken))
                {
                    return existing;
                }

                if (attempt == MaxAuthorizationWriteAttempts)
                {
                    TellmaPrincipalFactoryLog.GaveUpAuthorizationWrite(logger, clientObjectId, attempt);
                    return null;
                }

                TellmaPrincipalFactoryLog.LostAuthorizationWrite(logger, clientObjectId, attempt, MaxAuthorizationWriteAttempts);
                ForgetAuthorizationsRead();
            }
        }

        /// <summary>
        ///     Stops tracking every authorization this request's store context has read, so the
        ///     next read materializes them from the database. A tracked copy is otherwise handed
        ///     back as it is by any later query, and the store leaves the copy whose write it
        ///     refused holding this grant's own edits and a concurrency token the database never
        ///     saw: a retry over it would see its own lost write as recorded, or lose again.
        /// </summary>
        private void ForgetAuthorizationsRead()
        {
            // Only unchanged copies are dropped: a pending change belongs to whoever made it, and
            // detaching it would discard it silently. The store marks a refused copy unchanged.
            foreach (EntityEntry<OpenIddictEntityFrameworkCoreAuthorization> entry in dbContext.ChangeTracker
                .Entries<OpenIddictEntityFrameworkCoreAuthorization>()
                .Where(static entry => entry.State == EntityState.Unchanged)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        /// <summary>
        ///     Finds the user's valid permanent authorizations for the client that hold the scopes,
        ///     and returns the first that also covers the resources, and the last one found
        ///     whatever it covers (the one a grant extends when none covers it).
        /// </summary>
        private async Task<(object? Covering, object? Existing)> FindAuthorizationAsync(
            string subject,
            string clientObjectId,
            ImmutableArray<string> scopes,
            IReadOnlyCollection<string> resources,
            CancellationToken cancellationToken)
        {
            // Read the whole list before reading any properties: the store streams this query
            // over the request's single connection, which does not allow a second active reader.
            List<object> candidates = [];
            await foreach (object candidate in authorizationManager.FindAsync(
                subject: subject,
                client: clientObjectId,
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: scopes,
                cancellationToken))
            {
                candidates.Add(candidate);
            }

            foreach (object candidate in candidates)
            {
                if (AuthorizationResources.Covers(
                    await authorizationManager.GetPropertiesAsync(candidate, cancellationToken), resources))
                {
                    return (candidate, candidate);
                }
            }

            return (null, candidates.Count > 0 ? candidates[^1] : null);
        }

        /// <summary>
        ///     Adds resources to an authorization's covered set. When a concurrent grant changed the
        ///     same authorization first, this write is lost rather than recorded over a stale copy,
        ///     and the caller reads the authorization again.
        /// </summary>
        /// <returns>Whether the resources were recorded.</returns>
        private async Task<bool> TryAddResourcesAsync(
            object authorization, IReadOnlyCollection<string> resources, CancellationToken cancellationToken)
        {
            OpenIddictAuthorizationDescriptor descriptor = new();
            await authorizationManager.PopulateAsync(descriptor, authorization, cancellationToken);
            AuthorizationResources.Write(
                descriptor.Properties,
                AuthorizationResources.Read(descriptor.Properties).Union(resources, StringComparer.Ordinal));

            try
            {
                await authorizationManager.UpdateAsync(authorization, descriptor, cancellationToken);
                return true;
            }
            catch (OpenIddictExceptions.ConcurrencyException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Creates the permanent authorization of a first grant: what the manager's own overload
        ///     builds, plus the resources it covers.
        /// </summary>
        private async Task<object> CreateAuthorizationAsync(
            ClaimsIdentity identity,
            string subject,
            string clientObjectId,
            IReadOnlyCollection<string> resources,
            CancellationToken cancellationToken)
        {
            OpenIddictAuthorizationDescriptor descriptor = new()
            {
                ApplicationId = clientObjectId,
                CreationDate = timeProvider.GetUtcNow(),
                Principal = new ClaimsPrincipal(identity),
                Status = Statuses.Valid,
                Subject = subject,
                Type = AuthorizationTypes.Permanent,
            };
            descriptor.Scopes.UnionWith(identity.GetScopes());
            AuthorizationResources.Write(descriptor.Properties, resources);

            return await authorizationManager.CreateAsync(descriptor, cancellationToken);
        }

        /// <summary>
        ///     Derives token audiences. Explicit <c>resource</c> parameters — already validated
        ///     against the client's grants by the engine's resource check, per-tenant resources and
        ///     combinations included — are the whole audience. Without one, a browser client's
        ///     distribution audience derives from its registered origin and is likewise the whole
        ///     audience; otherwise the token carries its scopes' fixed platform audiences. A
        ///     distribution's audience is never combined with a platform one, so a token handed to
        ///     a distribution is never also a credential at the identity server's own APIs.
        /// </summary>
        private async Task<string?> SetAudiencesAsync(
            ClaimsIdentity identity, GrantRequest grant, object application, CancellationToken cancellationToken)
        {
            List<string> resources = [.. grant.Resources];
            if (resources.Count == 0 && identity.HasScope(TellmaIdentityConstants.ApiScope))
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                string? origin = TellmaClientProperties.Get(properties, TellmaClientProperties.Origin);
                if (origin is null)
                {
                    return "The tellma_api scope requires an explicit 'resource' parameter for this client.";
                }

                // The derived audience is a distribution's, so the management scope cannot ride
                // with it; the resource check refuses the explicit form of the same request.
                if (identity.HasScope(TellmaIdentityConstants.IdentityScope))
                {
                    return "A distribution audience cannot be combined with the tellma_identity scope; request two tokens.";
                }

                resources.Add(origin);
            }

            // Without a distribution audience, the token is for the platform's own APIs: the
            // audiences of its management and operator scopes.
            if (resources.Count == 0)
            {
                ImmutableArray<string> platformScopes =
                    [.. identity.GetScopes().Where(static scope => scope != TellmaIdentityConstants.ApiScope)];
                await foreach (string resource in scopeManager.ListResourcesAsync(platformScopes, cancellationToken))
                {
                    if (!resources.Contains(resource, StringComparer.Ordinal))
                    {
                        resources.Add(resource);
                    }
                }
            }

            identity.SetResources(resources);
            return null;
        }
    }

    /// <summary>Log messages of <see cref="TellmaPrincipalFactory" />.</summary>
    internal static partial class TellmaPrincipalFactoryLog
    {
        /// <summary>A grant lost its write to the authorization it extends; it reads it again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="applicationId">The client's application object id.</param>
        /// <param name="attempt">The attempt that lost.</param>
        /// <param name="maxAttempts">The bound on attempts.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "A grant for application {ApplicationId} lost its authorization write to a concurrent grant on attempt {Attempt} of {MaxAttempts}; reading the authorization again.")]
        public static partial void LostAuthorizationWrite(ILogger logger, string applicationId, int attempt, int maxAttempts);

        /// <summary>A grant lost every attempt at its authorization write, and was refused.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="applicationId">The client's application object id.</param>
        /// <param name="attempts">The attempts made.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "A grant for application {ApplicationId} lost its authorization write to concurrent grants {Attempts} times; it was refused with temporarily_unavailable.")]
        public static partial void GaveUpAuthorizationWrite(ILogger logger, string applicationId, int attempts);
    }
}
