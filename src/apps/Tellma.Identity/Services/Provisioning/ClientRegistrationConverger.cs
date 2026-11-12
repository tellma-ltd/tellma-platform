// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Text.Json;
using Tellma.Identity.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     Converges the registrations the platform owns — seeded clients, distributions and scopes —
    ///     on the values that decide them. The one write path that onboarding and startup seeding
    ///     share: a registration that already matches is left untouched, a changed one is rewritten,
    ///     and nothing is ever deleted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Several instances of a host start together — after a secret rotation, or on a first
    ///         deployment — and converge the same registrations at once. Every write either lands
    ///         or loses to a concurrent writer: an update to a row another instance changed meanwhile
    ///         fails its concurrency check, and a create of a row another instance created meanwhile
    ///         fails validation or the unique index. A lost write is not an error here. Each write
    ///         point — one application, one scope, one grant — is converged on its own and retried
    ///         on its own: the loser re-reads the row in a fresh service scope, so no stale tracked
    ///         entity or cached application survives, finds the winner's values and accepts or
    ///         rewrites them. A convergence with more write points therefore tolerates more lost
    ///         writes in total, <see cref="MaxAttemptsPerWrite" /> at each. A conflict that persists
    ///         past that bound at one point is thrown.
    ///     </para>
    ///     <para>
    ///         Every attempt runs in a scope of its own, never the caller's, so a lost write leaves
    ///         nothing tracked in the caller's store context for its next save to re-flush.
    ///     </para>
    /// </remarks>
    /// <param name="applicationManager">The OpenIddict application manager of this scope.</param>
    /// <param name="scopeManager">The OpenIddict scope manager of this scope.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="scopeFactory">Creates the fresh scope each attempt runs in.</param>
    /// <param name="logger">Logs lost writes and their retries.</param>
    public sealed class ClientRegistrationConverger(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IOptions<TellmaIdentityOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ClientRegistrationConverger> logger)
    {
        /// <summary>
        ///     How many times one write point (an application, a scope, a grant) is attempted before a
        ///     conflict that persists there is thrown. Each point loses only to a writer of that same
        ///     row, and the re-read that follows a loss sees the winner, so a handful is generous.
        /// </summary>
        public const int MaxAttemptsPerWrite = 4;

        /// <summary>SQL Server's errors for a duplicate key in a unique index and in a unique constraint.</summary>
        private static readonly int[] DuplicateKeyErrors = [2601, 2627];

        /// <summary>
        ///     Converges a distribution's two registrations through onboarding's own steps: the BFF
        ///     and backend clients built by <see cref="ClientDescriptorFactory" />, the origin added to
        ///     the distribution API's audiences, and the origin granted to the seeded platform
        ///     clients that call distribution APIs. Running it again with the same values writes
        ///     nothing; a changed secret, origin or redirect list is applied. It never deletes an
        ///     application, and converges the two it names whoever created them. A moved origin
        ///     retires the old one, unless another registration still carries it: it leaves the
        ///     distribution API's audiences — the list the control plane, document clients and the
        ///     per-tenant pattern are evaluated against — and the platform clients' grants.
        /// </summary>
        /// <param name="registration">The registration values, secrets included.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>What the convergence wrote, across every attempt it took.</returns>
        public async Task<DistributionConvergence> ConvergeDistributionAsync(
            DistributionClientRegistration registration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(registration);

            string originValue = registration.Origin.GetLeftPart(UriPartial.Authority);

            // The origin the distribution's BFF client carries now, read before its clients are
            // rewritten: when this convergence moves it, the old one is retired below.
            string? previousOrigin = await WithRetryAsync(
                registration.Slug,
                converger => converger.ReadClientOriginAsync(registration.Slug, cancellationToken),
                cancellationToken);

            // Each write point converges, and retries, on its own (see the remarks), so a lost
            // write costs one re-read of that point and never a re-run of the points before it.
            ClientConvergence client = await WithRetryAsync(
                registration.Slug,
                converger => converger.ConvergeApplicationAsync(
                    ClientDescriptorFactory.Distribution(
                        registration.Slug, registration.Origin, registration.BackchannelLogoutUri, registration.ClientSecret),
                    adjust: null,
                    cancellationToken),
                cancellationToken);
            ClientConvergence serviceClient = await WithRetryAsync(
                registration.ServiceClientId,
                converger => converger.ConvergeApplicationAsync(
                    ClientDescriptorFactory.DistributionService(
                        registration.Slug,
                        registration.Origin,
                        registration.ServiceClientSecret,
                        registration.AllowTokenExchange),
                    adjust: null,
                    cancellationToken),
                cancellationToken);

            // The origin becomes a requestable audience: recorded on the tellma_api scope entity
            // (the audience registry) and granted to the seeded platform clients so the CLI and
            // native apps may name it via the `resource` parameter.
            bool audienceAdded = await WithRetryAsync(
                TellmaIdentityConstants.ApiScope,
                converger => converger.AppendScopeResourceAsync(TellmaIdentityConstants.ApiScope, originValue, cancellationToken),
                cancellationToken);

            int grantsAdded = 0;
            foreach (string clientId in await WithRetryAsync(
                registration.Slug,
                converger => converger.ListClientsCallingDistributionApisAsync(cancellationToken),
                cancellationToken))
            {
                if (await WithRetryAsync(
                    clientId,
                    converger => converger.GrantResourceAsync(clientId, originValue, cancellationToken),
                    cancellationToken))
                {
                    grantsAdded++;
                }
            }

            bool moved = previousOrigin is not null && !string.Equals(previousOrigin, originValue, StringComparison.Ordinal);
            string? retiredOrigin = moved && await RetireOriginAsync(registration.Slug, previousOrigin!, originValue, cancellationToken)
                ? previousOrigin
                : null;

            return new DistributionConvergence(client, serviceClient, audienceAdded, grantsAdded, retiredOrigin);
        }

        /// <summary>
        ///     Converges one seeded platform client (<c>Cli</c>, <c>Native</c> or <c>ControlPlane</c>)
        ///     on its descriptor. A client that calls distribution APIs keeps the distribution
        ///     audiences onboarding granted it: they are not in its seed entry, and dropping them
        ///     would cut it off from every distribution.
        /// </summary>
        /// <param name="seed">The seed entry.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>What happened to the stored registration.</returns>
        public async Task<ClientConvergence> ConvergeSeededClientAsync(
            TellmaIdentitySeedClientOptions seed, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(seed);

            return await WithRetryAsync(
                seed.ClientId ?? string.Empty,
                converger => converger.ConvergeSeededClientOnceAsync(seed, cancellationToken),
                cancellationToken);
        }

        /// <summary>
        ///     Converges one scope: creates it with its display name and resources, or brings an
        ///     existing one's resources in line. By default it only adds the resources the scope
        ///     lacks, since onboarding appends distribution audiences to the distribution API scope
        ///     at runtime; a scope whose audiences the platform alone fixes converges on exactly
        ///     <paramref name="resources" />, so an audience it no longer grants is removed.
        /// </summary>
        /// <param name="name">The scope name.</param>
        /// <param name="displayName">The display name a new scope is created with.</param>
        /// <param name="resources">The resources the scope must hold.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <param name="exact">Converge on exactly these resources, removing any other.</param>
        /// <returns>Whether anything was written.</returns>
        public async Task<bool> ConvergeScopeAsync(
            string name,
            string displayName,
            IReadOnlyCollection<string> resources,
            CancellationToken cancellationToken,
            bool exact = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(resources);

            return await WithRetryAsync(
                name,
                converger => converger.ConvergeScopeOnceAsync(name, displayName, resources, exact, cancellationToken),
                cancellationToken);
        }

        /// <summary>
        ///     Whether two descriptors describe the same registration in every member the platform
        ///     sets, compared as sets where order carries no meaning. Secrets are compared as stored.
        /// </summary>
        /// <param name="left">One descriptor.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when writing one over the other would change nothing.</returns>
        internal static bool HaveSameRegistration(OpenIddictApplicationDescriptor left, OpenIddictApplicationDescriptor right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);

            return string.Equals(left.ClientId, right.ClientId, StringComparison.Ordinal)
                && string.Equals(left.ClientSecret, right.ClientSecret, StringComparison.Ordinal)
                && string.Equals(left.ClientType, right.ClientType, StringComparison.Ordinal)
                && string.Equals(left.ApplicationType, right.ApplicationType, StringComparison.Ordinal)
                && string.Equals(left.ConsentType, right.ConsentType, StringComparison.Ordinal)
                && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                && left.DisplayNames.Count == right.DisplayNames.Count
                && left.DisplayNames.All(pair => right.DisplayNames.TryGetValue(pair.Key, out string? name)
                    && string.Equals(pair.Value, name, StringComparison.Ordinal))
                && left.JsonWebKeySet is null && right.JsonWebKeySet is null
                && left.Permissions.SetEquals(right.Permissions)
                && left.Requirements.SetEquals(right.Requirements)
                && left.RedirectUris.SetEquals(right.RedirectUris)
                && left.PostLogoutRedirectUris.SetEquals(right.PostLogoutRedirectUris)
                && left.Properties.Count == right.Properties.Count
                && left.Properties.All(pair => right.Properties.TryGetValue(pair.Key, out JsonElement value)
                    && JsonElement.DeepEquals(pair.Value, value))
                && left.Settings.Count == right.Settings.Count
                && left.Settings.All(pair => right.Settings.TryGetValue(pair.Key, out string? value)
                    && string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        /// <summary>
        ///     Whether a failed write lost a race with a concurrent writer rather than failing on its
        ///     own: a stale concurrency token, a duplicate key, or a create the manager refused
        ///     because the row appeared after this attempt looked. Any other validation failure is
        ///     the descriptor's own fault and is never retried.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>True when a re-read and another attempt may succeed.</returns>
        internal static bool IsConcurrentWriteConflict(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is OpenIddictExceptions.ConcurrencyException
                    or DbUpdateConcurrencyException
                    or ConcurrentCreateException
                    || (current is SqlException sql && DuplicateKeyErrors.Contains(sql.Number)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Runs one write point in a fresh scope, and again in another when it loses its write to
        ///     a concurrent writer, up to <see cref="MaxAttemptsPerWrite" /> attempts.
        /// </summary>
        private async Task<T> WithRetryAsync<T>(
            string subject, Func<ClientRegistrationConverger, Task<T>> attempt, CancellationToken cancellationToken)
        {
            for (int number = 1; ; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    return await attempt(scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>());
                }
                catch (Exception exception) when (number < MaxAttemptsPerWrite && IsConcurrentWriteConflict(exception))
                {
                    ClientRegistrationConvergerLog.LostConcurrentWrite(logger, subject, number, MaxAttemptsPerWrite, exception);
                }
            }
        }

        /// <summary>
        ///     Retires a moved distribution's old origin. The service accounts the distribution
        ///     created move with it (their owner origin and their audience become the new origin),
        ///     and the old origin leaves the distribution API's audiences and the grants of the
        ///     platform clients that call distribution APIs, so no client can mint a new grant for
        ///     it, nor for a tenant resource under it. An origin another distribution's clients
        ///     still carry is that distribution's: it stays, with a warning, and so do the service
        ///     accounts on it, whose owner cannot then be told apart.
        /// </summary>
        /// <returns>Whether the old origin was retired.</returns>
        private async Task<bool> RetireOriginAsync(
            string slug, string previousOrigin, string origin, CancellationToken cancellationToken)
        {
            List<(string ClientId, bool ServiceAccount)> carriers = await WithRetryAsync(
                slug,
                converger => converger.ListClientsWithOriginAsync(previousOrigin, cancellationToken),
                cancellationToken);
            int otherDistributions = carriers.Count(static carrier => !carrier.ServiceAccount);
            if (otherDistributions > 0)
            {
                ClientRegistrationConvergerLog.MovedOriginStillCarried(logger, slug, previousOrigin, origin, otherDistributions);
                return false;
            }

            // Ownership is recorded as the owner's origin, so an account left on the old one would
            // be invisible to its own distribution and mint tokens for a place it no longer is.
            foreach ((string clientId, _) in carriers)
            {
                await WithRetryAsync(
                    clientId,
                    converger => converger.MoveServiceAccountAsync(clientId, previousOrigin, origin, cancellationToken),
                    cancellationToken);
            }

            await WithRetryAsync(
                TellmaIdentityConstants.ApiScope,
                converger => converger.RemoveScopeResourceAsync(TellmaIdentityConstants.ApiScope, previousOrigin, cancellationToken),
                cancellationToken);

            foreach (string clientId in await WithRetryAsync(
                slug,
                converger => converger.ListClientsCallingDistributionApisAsync(cancellationToken),
                cancellationToken))
            {
                await WithRetryAsync(
                    clientId,
                    converger => converger.RevokeResourceAsync(clientId, previousOrigin, cancellationToken),
                    cancellationToken);
            }

            ClientRegistrationConvergerLog.OriginRetired(logger, slug, previousOrigin, origin);
            return true;
        }

        /// <summary>One attempt at converging a seeded platform client.</summary>
        private Task<ClientConvergence> ConvergeSeededClientOnceAsync(
            TellmaIdentitySeedClientOptions seed, CancellationToken cancellationToken)
        {
            // Re-seeding must not wipe the per-distribution resource permissions granted to the CLI
            // and native apps at provisioning time — but only those clients accumulate them, so a
            // client that never names distribution APIs (the control plane) never resurrects a
            // stale grant. The marker is read from the stored client OR the fresh descriptor: a
            // client seeded before the marker existed carries it only in the descriptor, and gating
            // on the stored copy alone would drop its accumulated grants on the first boot after
            // the upgrade.
            return ConvergeApplicationAsync(
                ClientDescriptorFactory.SeededClient(options.Value, seed),
                static (current, desired) =>
                {
                    if (TellmaClientProperties.IsSet(current.Properties, TellmaClientProperties.CallsDistributionApis)
                        || TellmaClientProperties.IsSet(desired.Properties, TellmaClientProperties.CallsDistributionApis))
                    {
                        desired.Permissions.UnionWith(current.Permissions.Where(static permission =>
                            permission.StartsWith(Permissions.Prefixes.Resource, StringComparison.Ordinal)));
                    }
                },
                cancellationToken);
        }

        /// <summary>One attempt at converging a scope.</summary>
        private async Task<bool> ConvergeScopeOnceAsync(
            string name, string displayName, IReadOnlyCollection<string> resources, bool exact, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(name, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor descriptor = new() { Name = name, DisplayName = displayName };
                descriptor.Resources.UnionWith(resources);
                await CreateScopeAsync(descriptor, cancellationToken);
                return true;
            }

            OpenIddictScopeDescriptor existing = new();
            await scopeManager.PopulateAsync(existing, scope, cancellationToken);
            if (exact ? existing.Resources.SetEquals(resources) : resources.All(existing.Resources.Contains))
            {
                return false;
            }

            if (exact)
            {
                existing.Resources.Clear();
            }

            existing.Resources.UnionWith(resources);
            await scopeManager.UpdateAsync(scope, existing, cancellationToken);
            return true;
        }

        /// <summary>
        ///     Creates a client registration, or rewrites an existing one with the same client id to
        ///     match the descriptor. A registration that already matches — its secret included — is
        ///     left untouched, so no write happens and no fresh hash is stored.
        /// </summary>
        /// <param name="descriptor">The desired registration, with its secret in plain text.</param>
        /// <param name="adjust">Folds what must survive from the stored registration into the desired one.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>What happened to the stored registration.</returns>
        internal async Task<ClientConvergence> ConvergeApplicationAsync(
            OpenIddictApplicationDescriptor descriptor,
            Action<OpenIddictApplicationDescriptor, OpenIddictApplicationDescriptor>? adjust,
            CancellationToken cancellationToken)
        {
            object? existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                try
                {
                    await applicationManager.CreateAsync(descriptor, cancellationToken);
                }
                catch (OpenIddictExceptions.ValidationException exception)
                {
                    // The client id was free when this attempt looked and taken when it wrote: a
                    // lost race. Any other refusal is the descriptor's own and surfaces as is.
                    if (await ApplicationExistsAsync(descriptor.ClientId!, cancellationToken))
                    {
                        throw new ConcurrentCreateException(descriptor.ClientId!, exception);
                    }

                    throw;
                }

                return ClientConvergence.Created;
            }

            OpenIddictApplicationDescriptor current = new();
            await applicationManager.PopulateAsync(current, existing, cancellationToken);
            adjust?.Invoke(current, descriptor);

            // The stored secret is a salted hash, so equality is decided by validating the plain
            // secret against it. A matching secret is carried as the stored hash: the manager
            // re-hashes only a secret that differs from what it holds, so an update made for
            // another reason keeps the hash, and a no-op keeps everything.
            bool secretMatches = descriptor.ClientSecret is null
                ? current.ClientSecret is null
                : await applicationManager.ValidateClientSecretAsync(existing, descriptor.ClientSecret, cancellationToken);
            if (secretMatches)
            {
                descriptor.ClientSecret = current.ClientSecret;
                if (HaveSameRegistration(current, descriptor))
                {
                    return ClientConvergence.Unchanged;
                }
            }

            await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
            return ClientConvergence.Updated;
        }

        /// <summary>Appends one resource to a scope entity's resource registry.</summary>
        /// <returns>Whether the resource was new to the scope.</returns>
        internal async Task<bool> AppendScopeResourceAsync(string scopeName, string resource, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor created = new() { Name = scopeName };
                created.Resources.Add(resource);
                await CreateScopeAsync(created, cancellationToken);
                return true;
            }

            OpenIddictScopeDescriptor descriptor = new();
            await scopeManager.PopulateAsync(descriptor, scope, cancellationToken);
            if (!descriptor.Resources.Add(resource))
            {
                return false;
            }

            await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
            return true;
        }

        /// <summary>
        ///     The client ids of the platform clients that may name distribution APIs (CLI, native
        ///     apps): the clients a distribution's audience is granted to. The control plane is
        ///     deliberately excluded: its only audience is the control-plane surface, so a compromised
        ///     control-plane secret cannot mint tokens for a distribution API.
        /// </summary>
        /// <returns>The client ids.</returns>
        internal async Task<List<string>> ListClientsCallingDistributionApisAsync(CancellationToken cancellationToken)
        {
            // Read the whole list before writing any of it. The store streams this query over the
            // request's single connection, and multiple active result sets are not enabled, so an
            // update issued while that reader is still open fails outright.
            List<object> applications = [];
            await foreach (object application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            {
                applications.Add(application);
            }

            List<string> clientIds = [];
            foreach (object application in applications)
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                if (TellmaClientProperties.IsSet(properties, TellmaClientProperties.CallsDistributionApis)
                    && await applicationManager.GetClientIdAsync(application, cancellationToken) is { } clientId)
                {
                    clientIds.Add(clientId);
                }
            }

            return clientIds;
        }

        /// <summary>Grants one resource to one client, if it lacks it.</summary>
        /// <returns>Whether the client was newly granted the resource.</returns>
        internal async Task<bool> GrantResourceAsync(string clientId, string resource, CancellationToken cancellationToken)
        {
            object? application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is null)
            {
                return false;
            }

            OpenIddictApplicationDescriptor descriptor = new();
            await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
            if (!descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource))
            {
                return false;
            }

            await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
            return true;
        }

        /// <summary>The origin a client's stored registration carries.</summary>
        /// <returns>The origin, or null when the client does not exist or carries none.</returns>
        internal async Task<string?> ReadClientOriginAsync(string clientId, CancellationToken cancellationToken)
        {
            object? application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            return application is null
                ? null
                : TellmaClientProperties.Get(
                    await applicationManager.GetPropertiesAsync(application, cancellationToken), TellmaClientProperties.Origin);
        }

        /// <summary>
        ///     Every registration that carries an origin — a distribution's clients and the service
        ///     accounts it created — with whether each is a service account.
        /// </summary>
        /// <returns>The client ids, each with its service-account marker.</returns>
        internal async Task<List<(string ClientId, bool ServiceAccount)>> ListClientsWithOriginAsync(
            string origin, CancellationToken cancellationToken)
        {
            // Read the whole list before reading any of its properties, for the reason given in
            // ListClientsCallingDistributionApisAsync.
            List<object> applications = [];
            await foreach (object application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            {
                applications.Add(application);
            }

            List<(string ClientId, bool ServiceAccount)> carriers = [];
            foreach (object application in applications)
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                if (string.Equals(TellmaClientProperties.Get(properties, TellmaClientProperties.Origin), origin, StringComparison.Ordinal)
                    && await applicationManager.GetClientIdAsync(application, cancellationToken) is { } clientId)
                {
                    carriers.Add((clientId, TellmaClientProperties.IsSet(properties, TellmaClientProperties.ServiceAccount)));
                }
            }

            return carriers;
        }

        /// <summary>
        ///     Moves one service account from its owner's old origin to the new one: the recorded
        ///     owner origin, and the old origin's grant replaced by the new one's.
        /// </summary>
        /// <returns>Whether anything was written.</returns>
        internal async Task<bool> MoveServiceAccountAsync(
            string clientId, string previousOrigin, string origin, CancellationToken cancellationToken)
        {
            object? application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is null)
            {
                return false;
            }

            OpenIddictApplicationDescriptor descriptor = new();
            await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
            if (!TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.ServiceAccount)
                || !string.Equals(
                    TellmaClientProperties.Get(descriptor.Properties, TellmaClientProperties.Origin), previousOrigin, StringComparison.Ordinal))
            {
                return false;
            }

            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Origin, origin);
            if (descriptor.Permissions.Remove(Permissions.Prefixes.Resource + previousOrigin))
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Resource + origin);
            }

            await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
            return true;
        }

        /// <summary>Removes one resource from a scope entity's resource registry.</summary>
        /// <returns>Whether the scope held the resource.</returns>
        internal async Task<bool> RemoveScopeResourceAsync(string scopeName, string resource, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (scope is null)
            {
                return false;
            }

            OpenIddictScopeDescriptor descriptor = new();
            await scopeManager.PopulateAsync(descriptor, scope, cancellationToken);
            if (!descriptor.Resources.Remove(resource))
            {
                return false;
            }

            await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
            return true;
        }

        /// <summary>Revokes one resource from one client, if it holds it.</summary>
        /// <returns>Whether the client held the resource.</returns>
        internal async Task<bool> RevokeResourceAsync(string clientId, string resource, CancellationToken cancellationToken)
        {
            object? application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is null)
            {
                return false;
            }

            OpenIddictApplicationDescriptor descriptor = new();
            await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
            if (!descriptor.Permissions.Remove(Permissions.Prefixes.Resource + resource))
            {
                return false;
            }

            await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
            return true;
        }

        /// <summary>Creates a scope, reporting a name taken since the attempt looked as a lost race.</summary>
        private async Task CreateScopeAsync(OpenIddictScopeDescriptor descriptor, CancellationToken cancellationToken)
        {
            try
            {
                await scopeManager.CreateAsync(descriptor, cancellationToken);
            }
            catch (OpenIddictExceptions.ValidationException exception)
            {
                if (await ScopeExistsAsync(descriptor.Name!, cancellationToken))
                {
                    throw new ConcurrentCreateException(descriptor.Name!, exception);
                }

                throw;
            }
        }

        /// <summary>
        ///     Whether a client id is registered now, read in a fresh scope: this scope's manager
        ///     caches the miss it saw before the create, while its validation read the store itself.
        /// </summary>
        private async Task<bool> ApplicationExistsAsync(string clientId, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>()
                .FindByClientIdAsync(clientId, cancellationToken) is not null;
        }

        /// <summary>Whether a scope name is registered now, read in a fresh scope (see <see cref="ApplicationExistsAsync" />).</summary>
        private async Task<bool> ScopeExistsAsync(string name, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>()
                .FindByNameAsync(name, cancellationToken) is not null;
        }

        /// <summary>
        ///     A create the manager refused because a concurrent writer created the same row after
        ///     this attempt looked for it: a lost race, retried, never a validation failure.
        /// </summary>
        /// <param name="subject">The client id or scope name.</param>
        /// <param name="innerException">The manager's validation failure.</param>
        private sealed class ConcurrentCreateException(string subject, Exception innerException)
            : Exception($"'{subject}' was created by a concurrent writer.", innerException);
    }

    /// <summary>Source-generated log messages for <see cref="ClientRegistrationConverger" />.</summary>
    internal static partial class ClientRegistrationConvergerLog
    {
        /// <summary>A write lost to a concurrent writer; that write point is converged again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="subject">The client id or scope name being converged.</param>
        /// <param name="attempt">The attempt that lost.</param>
        /// <param name="maxAttempts">The bound on attempts.</param>
        /// <param name="exception">The conflict.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Converging {Subject} lost a write to a concurrent writer on attempt {Attempt} of {MaxAttempts}; converging again.")]
        public static partial void LostConcurrentWrite(
            ILogger logger, string subject, int attempt, int maxAttempts, Exception exception);

        /// <summary>A distribution moved, and its old origin was retired from the audiences and grants.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="previousOrigin">The retired origin.</param>
        /// <param name="origin">The distribution's origin now.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Distribution {Slug} moved from {PreviousOrigin} to {Origin}; the old origin was retired from the distribution API's audiences and the platform clients' grants.")]
        public static partial void OriginRetired(ILogger logger, string slug, string previousOrigin, string origin);

        /// <summary>
        ///     A distribution moved, but its old origin stays an audience because another
        ///     distribution's clients carry it.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="previousOrigin">The old origin, still an audience.</param>
        /// <param name="origin">The distribution's origin now.</param>
        /// <param name="carriers">How many other distribution clients carry the old origin.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Distribution {Slug} moved from {PreviousOrigin} to {Origin}, but {Carriers} clients of another distribution carry the old origin, so it remains a distribution audience, and the service accounts on it stay where they are.")]
        public static partial void MovedOriginStillCarried(
            ILogger logger, string slug, string previousOrigin, string origin, int carriers);
    }
}
