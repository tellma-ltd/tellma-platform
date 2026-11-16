// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     The engine's <see cref="IClientMetadataDocumentResolver" />. A standing cached document
    ///     is served without a fetch. Otherwise one fetch per client id runs at a time, shared by
    ///     every request that needs it: the document, then for <c>private_key_jwt</c> its key set,
    ///     each under the fetch guards. The shared fetch runs on its own timeout, not on any one
    ///     request's, and records its own audit row and counter outcome.
    ///     <para>
    ///         Each shared fetch draws on one of a host's two budgets (<see cref="ClientMetadataFetchBudget" />):
    ///         the refresh of a client the engine already holds, a cached copy or a stored row,
    ///         draws on the refresh budget, once per client id per window; anything else, which
    ///         any caller can cause by naming a client id, draws on the probe budget. Spending the
    ///         probe budget therefore never stops a known client from refreshing.
    ///     </para>
    /// </summary>
    /// <param name="fetcher">The guarded fetch.</param>
    /// <param name="rateLimiter">The fetch limits.</param>
    /// <param name="cache">The document cache.</param>
    /// <param name="auditor">Writes the audit rows.</param>
    /// <param name="metrics">The engine's metrics.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="timeProvider">The clock expiry is judged on.</param>
    /// <param name="httpContextAccessor">The current request, whose address the audit rows carry.</param>
    /// <param name="scopeFactory">Creates the scope a stored row is looked up in.</param>
    public sealed class ClientMetadataDocumentResolver(
        ClientMetadataFetcher fetcher,
        ClientMetadataRateLimiter rateLimiter,
        ClientMetadataCache cache,
        ClientMetadataAuditor auditor,
        IdentityMetrics metrics,
        IOptions<TellmaIdentityOptions> options,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory) : IClientMetadataDocumentResolver
    {
        /// <summary>The fetches in flight, by client id; each is removed when it completes.</summary>
        private readonly ConcurrentDictionary<string, Lazy<Task<ClientMetadataDocument>>> _flights = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public Task<ClientMetadataDocument?> ResolveAsync(Uri clientId, CancellationToken cancellationToken = default)
        {
            return ResolveCoreAsync(clientId, revalidate: false, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ClientMetadataDocument?> RevalidateAsync(Uri clientId, CancellationToken cancellationToken = default)
        {
            return ResolveCoreAsync(clientId, revalidate: true, cancellationToken);
        }

        /// <summary>Resolves from the cache or a shared fetch.</summary>
        private async Task<ClientMetadataDocument?> ResolveCoreAsync(Uri clientId, bool revalidate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            string raw = clientId.OriginalString;
            if (!ClientIdUrl.TryRecognize(raw, out Uri? url))
            {
                return null;
            }

            string? ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            // Refusals before any fetch: the feature is off, or the client id itself is not one
            // the engine will fetch or store.
            string? problem = options.Value.ClientMetadata.Enabled
                ? ClientIdUrl.FindProblem(url)
                : ClientMetadataRejectionReasons.Disabled;
            if (problem is not null)
            {
                metrics.ClientMetadataFetch(ClientMetadataFetchOutcomes.Rejected);
                await auditor.RejectedAsync(raw, problem, url.IdnHost, ipAddress);
                throw new ClientMetadataException(problem, $"The client id '{raw}' is refused.");
            }

            // A standing copy answers unless the caller asked for a fresh one.
            return !revalidate
                && cache.TryGet(raw, out ClientMetadataDocument? cached)
                && cached.ExpiresAt > timeProvider.GetUtcNow()
                    ? cached
                    : await JoinFlight(url, revalidate, ipAddress).WaitAsync(cancellationToken);
        }

        /// <summary>Joins the client id's fetch in flight, starting one if none is.</summary>
        private Task<ClientMetadataDocument> JoinFlight(Uri url, bool revalidate, string? ipAddress)
        {
            string key = url.OriginalString;
            Lazy<Task<ClientMetadataDocument>> created = new(() => FetchAsync(url, revalidate, ipAddress), LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<Task<ClientMetadataDocument>> flight = _flights.GetOrAdd(key, created);
            if (ReferenceEquals(flight, created))
            {
                // The flight leaves the table when it completes, so the next miss fetches again.
                // Its exception is observed here: every waiter may have stopped waiting.
                _ = flight.Value.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _flights.TryRemove(new KeyValuePair<string, Lazy<Task<ClientMetadataDocument>>>(key, created));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return flight.Value;
        }

        /// <summary>
        ///     The shared fetch: budget, slot, document, key set, validation, cache, audit and
        ///     counter. A fetch failure (a timeout, an unreachable host, a status other than 200, or
        ///     a spent limit) serves a standing cached copy if there is one; any other refusal drops
        ///     the cached copy, since the document now on the server is refused.
        /// </summary>
        private async Task<ClientMetadataDocument> FetchAsync(Uri url, bool revalidate, string? ipAddress)
        {
            // Off the caller's stack before the first fetch, so starting a flight never blocks
            // the request that started it on the table's lazy.
            await Task.Yield();

            string key = url.OriginalString;
            try
            {
                ClientMetadataFetchBudget budget = await ChooseBudgetAsync(key, revalidate);
                ClientMetadataDocument document = await FetchInSlotAsync(url, budget);
                cache.Set(document, timeProvider.GetUtcNow());
                metrics.ClientMetadataFetch(ClientMetadataFetchOutcomes.Fetched);
                await auditor.FetchedAsync(key, url.IdnHost, ipAddress);
                return document;
            }
            catch (ClientMetadataException exception)
            {
                bool servable = exception.IsFetchFailure || exception.Reason is ClientMetadataRejectionReasons.RateLimited;
                ClientMetadataDocument? standing = servable
                    && cache.TryGet(key, out ClientMetadataDocument? found)
                    && found.ExpiresAt > timeProvider.GetUtcNow()
                        ? found
                        : null;
                if (!servable)
                {
                    cache.Remove(key);
                }

                metrics.ClientMetadataFetch(exception.IsFetchFailure ? ClientMetadataFetchOutcomes.Failed : ClientMetadataFetchOutcomes.Rejected);
                await auditor.RejectedAsync(key, exception.Reason, url.IdnHost, ipAddress, servedCached: standing is not null);
                if (standing is not null)
                {
                    return standing;
                }

                throw;
            }
        }

        /// <summary>
        ///     The budget a shared fetch draws on: the refresh budget for a client the engine
        ///     already holds, when the client id has not drawn on it inside the window; otherwise
        ///     the probe budget. An early re-fetch is always a probe, since any caller can ask for
        ///     one by naming an unlisted redirect URI.
        /// </summary>
        private async Task<ClientMetadataFetchBudget> ChooseBudgetAsync(string clientId, bool revalidate)
        {
            if (revalidate)
            {
                return ClientMetadataFetchBudget.Probe;
            }

            // Only a fetch that succeeded leaves a cached copy or a row, so a client id holding
            // either was published by its host; the row is looked up only when the cache has
            // forgotten the client, as after a restart. The row must hold the id exactly: under
            // the store's case-insensitive comparison, another spelling is a different client id.
            bool known = cache.TryGet(clientId, out _) || await IsStoredAsync(clientId);
            return known && rateLimiter.TryClaimRefresh(clientId)
                ? ClientMetadataFetchBudget.Refresh
                : ClientMetadataFetchBudget.Probe;
        }

        /// <summary>Whether a row holds exactly this client id.</summary>
        private async Task<bool> IsStoredAsync(string clientId)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            string? stored = await scope.ServiceProvider.GetRequiredService<IDocumentClientStore>()
                .FindClientIdAsync(clientId, CancellationToken.None);
            return string.Equals(stored, clientId, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Fetches and validates the document and its key set inside one of the budget's
        ///     resolution slots, which bounds how many resolutions run at once.
        /// </summary>
        private async Task<ClientMetadataDocument> FetchInSlotAsync(Uri url, ClientMetadataFetchBudget budget)
        {
            if (!await rateLimiter.TryEnterAsync(budget))
            {
                throw new ClientMetadataException(
                    ClientMetadataRejectionReasons.RateLimited,
                    $"No fetch slot is free for '{url}'.");
            }

            try
            {
                TellmaIdentityClientMetadataOptions settings = options.Value.ClientMetadata;
                ClientMetadataFetchResult documentFetch = await fetcher.FetchAsync(url, ClientMetadataResource.Document, budget, CancellationToken.None);
                ParsedClientMetadata parsed = ClientMetadataDocumentParser.ParseDocument(url, documentFetch.Body);
                TimeSpan lifetime = ClientMetadataCacheLifetime.Clamp(documentFetch.Lifetime, settings.MinCache, settings.MaxCache);

                // A private_key_jwt client's keys stand only as long as both answers do. The key
                // set is on the document's own origin, so it draws on the same host budget.
                JsonWebKeySet? keys = null;
                if (parsed.JwksUri is { } jwksUri)
                {
                    ClientMetadataFetchResult keyFetch = await fetcher.FetchAsync(jwksUri, ClientMetadataResource.KeySet, budget, CancellationToken.None);
                    keys = ClientMetadataDocumentParser.ParseKeySet(keyFetch.Body);
                    TimeSpan keyLifetime = ClientMetadataCacheLifetime.Clamp(keyFetch.Lifetime, settings.MinCache, settings.MaxCache);
                    lifetime = keyLifetime < lifetime ? keyLifetime : lifetime;
                }

                return new ClientMetadataDocument(
                    url,
                    parsed.ClientName,
                    parsed.RedirectUris,
                    parsed.TokenEndpointAuthMethod,
                    parsed.JwksUri,
                    keys,
                    documentFetch.FetchedAt,
                    documentFetch.FetchedAt + lifetime);
            }
            finally
            {
                rateLimiter.Exit(budget);
            }
        }
    }
}
