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
    ///         fails validation or the unique index. A lost write is never an error here. The whole
    ///         convergence is re-run, in a fresh service scope so no stale tracked entity or cached
    ///         application survives into it, and on the re-run it finds the winner's row and either
    ///         accepts it or rewrites it. The attempts are bounded; a conflict that persists past them
    ///         is thrown.
    ///     </para>
    ///     <para>
    ///         Every attempt runs in a scope of its own, never the caller's, so a lost write leaves
    ///         nothing tracked in the caller's store context for its next save to re-flush.
    ///     </para>
    /// </remarks>
    /// <param name="applicationManager">The OpenIddict application manager of this scope.</param>
    /// <param name="scopeManager">The OpenIddict scope manager of this scope.</param>
    /// <param name="options">The engine options (the issuer origin).</param>
    /// <param name="scopeFactory">Creates the fresh scope each attempt runs in.</param>
    /// <param name="logger">Logs lost writes and their retries.</param>
    public sealed class ClientRegistrationConverger(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IOptions<TellmaIdentityOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ClientRegistrationConverger> logger)
    {
        /// <summary>How many times one convergence is attempted before a persisting conflict is thrown.</summary>
        public const int MaxAttempts = 4;

        /// <summary>SQL Server's errors for a duplicate key in a unique index and in a unique constraint.</summary>
        private static readonly int[] DuplicateKeyErrors = [2601, 2627];

        /// <summary>
        ///     Converges a distribution's two registrations through onboarding's own steps: the BFF
        ///     and backend clients built by <see cref="ClientDescriptorFactory" />, the origin added to
        ///     the distribution API's audiences, and the origin granted to the seeded platform
        ///     clients that call distribution APIs. Running it again with the same values writes
        ///     nothing; a changed secret, origin or redirect list is applied. It never deletes an
        ///     application, and converges the two it names whoever created them.
        /// </summary>
        /// <param name="registration">The registration values, secrets included.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>What the convergence wrote, across every attempt it took.</returns>
        public async Task<DistributionConvergence> ConvergeDistributionAsync(
            DistributionClientRegistration registration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(registration);

            // What earlier, interrupted attempts wrote stays written; the progress keeps it, so the
            // outcome reports a client this instance created even if a later step lost a race.
            DistributionProgress progress = new();
            await WithRetryAsync(
                registration.Slug,
                converger => converger.ConvergeDistributionOnceAsync(registration, progress, cancellationToken),
                cancellationToken);
            return progress.ToConvergence();
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

            ClientConvergence outcome = ClientConvergence.Unchanged;
            await WithRetryAsync(
                seed.ClientId ?? string.Empty,
                async converger =>
                {
                    outcome = Merge(outcome, await converger.ConvergeSeededClientOnceAsync(seed, cancellationToken));
                    return true;
                },
                cancellationToken);
            return outcome;
        }

        /// <summary>
        ///     Converges one scope: creates it with its display name and resources, or adds any of
        ///     the resources it lacks. Resources already on the scope stay, since onboarding appends
        ///     distribution audiences to it at runtime.
        /// </summary>
        /// <param name="name">The scope name.</param>
        /// <param name="displayName">The display name a new scope is created with.</param>
        /// <param name="resources">The resources the scope must hold.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>Whether anything was written.</returns>
        public async Task<bool> ConvergeScopeAsync(
            string name, string displayName, IReadOnlyCollection<string> resources, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(resources);

            bool changed = false;
            await WithRetryAsync(
                name,
                async converger =>
                {
                    changed |= await converger.ConvergeScopeOnceAsync(name, displayName, resources, cancellationToken);
                    return true;
                },
                cancellationToken);
            return changed;
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
        ///     own: a stale concurrency token, a duplicate key, or the manager's own validation
        ///     refusing a client id or scope name that appeared since this attempt looked. A
        ///     validation failure that is not a race fails identically on every attempt, so retrying
        ///     it costs a few bounded re-runs and then surfaces it unchanged.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>True when a re-run may succeed.</returns>
        internal static bool IsConcurrentWriteConflict(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is OpenIddictExceptions.ConcurrencyException
                    or OpenIddictExceptions.ValidationException
                    or DbUpdateConcurrencyException
                    || (current is SqlException sql && DuplicateKeyErrors.Contains(sql.Number)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The outcome that reports every write: a create or update by any attempt outranks a later no-op.</summary>
        private static ClientConvergence Merge(ClientConvergence earlier, ClientConvergence later)
        {
            return earlier == ClientConvergence.Unchanged ? later : earlier;
        }

        /// <summary>
        ///     Runs a convergence in a fresh scope, and again in another when it loses a write to a
        ///     concurrent writer, up to <see cref="MaxAttempts" /> attempts.
        /// </summary>
        private async Task WithRetryAsync(
            string subject, Func<ClientRegistrationConverger, Task<bool>> attempt, CancellationToken cancellationToken)
        {
            for (int number = 1; ; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    await attempt(scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>());
                    return;
                }
                catch (Exception exception) when (number < MaxAttempts && IsConcurrentWriteConflict(exception))
                {
                    ClientRegistrationConvergerLog.LostConcurrentWrite(logger, subject, number, MaxAttempts, exception);
                }
            }
        }

        /// <summary>One attempt at converging a distribution, recording each step's outcome in the progress.</summary>
        private async Task<bool> ConvergeDistributionOnceAsync(
            DistributionClientRegistration registration, DistributionProgress progress, CancellationToken cancellationToken)
        {
            string issuerOrigin = options.Value.Issuer!.GetLeftPart(UriPartial.Authority);
            string originValue = registration.Origin.GetLeftPart(UriPartial.Authority);

            progress.Client = Merge(progress.Client, await ConvergeApplicationAsync(
                ClientDescriptorFactory.Distribution(
                    registration.Slug, registration.Origin, registration.BackchannelLogoutUri, registration.ClientSecret),
                adjust: null,
                cancellationToken));
            progress.ServiceClient = Merge(progress.ServiceClient, await ConvergeApplicationAsync(
                ClientDescriptorFactory.DistributionService(
                    registration.Slug,
                    registration.Origin,
                    issuerOrigin,
                    registration.ServiceClientSecret,
                    registration.AllowTokenExchange),
                adjust: null,
                cancellationToken));

            // The origin becomes a requestable audience: recorded on the tellma_api scope entity
            // (the audience registry) and granted to the seeded platform clients so the CLI and
            // native apps may name it via the `resource` parameter.
            progress.AudienceAdded |= await AppendScopeResourceAsync(TellmaIdentityConstants.ApiScope, originValue, cancellationToken);
            progress.GrantsAdded += await GrantResourceToPlatformClientsAsync(originValue, cancellationToken);
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
            string name, string displayName, IReadOnlyCollection<string> resources, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(name, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor descriptor = new() { Name = name, DisplayName = displayName };
                descriptor.Resources.UnionWith(resources);
                await scopeManager.CreateAsync(descriptor, cancellationToken);
                return true;
            }

            OpenIddictScopeDescriptor existing = new();
            await scopeManager.PopulateAsync(existing, scope, cancellationToken);
            if (resources.All(existing.Resources.Contains))
            {
                return false;
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
        private async Task<ClientConvergence> ConvergeApplicationAsync(
            OpenIddictApplicationDescriptor descriptor,
            Action<OpenIddictApplicationDescriptor, OpenIddictApplicationDescriptor>? adjust,
            CancellationToken cancellationToken)
        {
            object? existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await applicationManager.CreateAsync(descriptor, cancellationToken);
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
        private async Task<bool> AppendScopeResourceAsync(string scopeName, string resource, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor created = new() { Name = scopeName };
                created.Resources.Add(resource);
                await scopeManager.CreateAsync(created, cancellationToken);
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
        ///     Grants a distribution's audience to the platform clients that may name distribution
        ///     APIs (CLI, native apps). The control plane is deliberately excluded: its only audience
        ///     is the control-plane surface, so a compromised control-plane secret cannot mint tokens
        ///     for a distribution API.
        /// </summary>
        /// <returns>How many clients were newly granted the resource.</returns>
        private async Task<int> GrantResourceToPlatformClientsAsync(string resource, CancellationToken cancellationToken)
        {
            string permission = Permissions.Prefixes.Resource + resource;

            // Read the whole list before writing any of it. The store streams this query over the
            // request's single connection, and multiple active result sets are not enabled, so an
            // update issued while that reader is still open fails outright. Only a client carrying
            // the marker is written, so a registry holding none never reached the write and never
            // showed the fault.
            List<object> applications = [];
            await foreach (object application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            {
                applications.Add(application);
            }

            int granted = 0;
            foreach (object application in applications)
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                if (!TellmaClientProperties.IsSet(properties, TellmaClientProperties.CallsDistributionApis))
                {
                    continue;
                }

                OpenIddictApplicationDescriptor descriptor = new();
                await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
                if (descriptor.Permissions.Add(permission))
                {
                    await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
                    granted++;
                }
            }

            return granted;
        }

        /// <summary>What a distribution's convergence wrote so far, across attempts.</summary>
        private sealed class DistributionProgress
        {
            /// <summary>The BFF client's outcome.</summary>
            public ClientConvergence Client { get; set; }

            /// <summary>The backend client's outcome.</summary>
            public ClientConvergence ServiceClient { get; set; }

            /// <summary>Whether the origin was added to the API audiences.</summary>
            public bool AudienceAdded { get; set; }

            /// <summary>How many platform clients were granted the origin.</summary>
            public int GrantsAdded { get; set; }

            /// <summary>The outcome as the caller sees it.</summary>
            /// <returns>The convergence.</returns>
            public DistributionConvergence ToConvergence()
            {
                return new DistributionConvergence(Client, ServiceClient, AudienceAdded, GrantsAdded);
            }
        }
    }

    /// <summary>Source-generated log messages for <see cref="ClientRegistrationConverger" />.</summary>
    internal static partial class ClientRegistrationConvergerLog
    {
        /// <summary>A write lost to a concurrent writer; the convergence runs again.</summary>
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
    }
}
