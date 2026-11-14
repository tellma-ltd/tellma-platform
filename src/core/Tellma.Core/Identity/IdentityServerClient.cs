// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Identity
{
    // Spec 0018 §2: the distribution's client of the identity server's v1 management API (spec 0010
    // §A.8), with the bulk members chunked and answered by prefix (§2.3), and every call counted,
    // timed and logged once (§2.2, §8.2).

    /// <summary>
    ///     The runtime <see cref="IIdentityServerClient" />: the identity server's v1 management API
    ///     over the named <c>Tellma.Identity</c> HTTP client, authenticated as the distribution's service
    ///     client with a cached <c>client_credentials</c> token.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every call is counted on <c>tellma.identity.calls</c>, timed on
    ///         <c>tellma.identity.call.duration</c> and logged once as <c>IdentityCall</c>; each chunk of
    ///         a bulk call is a trace activity of its own. No token, secret, Authorization header,
    ///         address or subject is ever logged.
    ///     </para>
    ///     <para>A singleton, safe to call from any thread.</para>
    /// </remarks>
    /// <param name="httpClients">Creates the named <c>Tellma.Identity</c> client.</param>
    /// <param name="options">The identity server's authority and the service client's credentials.</param>
    /// <param name="time">The clock of the token's expiry, each exchange's timeout and the calls' durations.</param>
    /// <param name="meterFactory">Creates the core meter the instruments are on.</param>
    /// <param name="logger">The client's logger.</param>
    public sealed class IdentityServerClient(
        IHttpClientFactory httpClients,
        IOptions<IdentityServerClientOptions> options,
        TimeProvider time,
        IMeterFactory meterFactory,
        ILogger<IdentityServerClient> logger) : IIdentityServerClient
    {
        /// <summary>The name of the <see cref="HttpClient" /> the client sends through.</summary>
        public const string HttpClientName = IdentityTransport.HttpClientName;

        /// <summary>The bulk invite, relative to the authority.</summary>
        internal const string InviteRoute = "api/identity/v1/invitations";

        /// <summary>The bulk delivery status, relative to the authority.</summary>
        internal const string DeliveryRoute = "api/identity/v1/invitations/delivery-status";

        /// <summary>The service accounts, relative to the authority.</summary>
        internal const string ServiceAccountsRoute = "api/identity/v1/service-accounts";

        /// <summary>The invite statuses the server answers, by their wire names.</summary>
        private static readonly FrozenDictionary<string, InviteStatus> InviteStatuses =
            Enum.GetValues<InviteStatus>().ToFrozenDictionary(static status => status.ToString(), StringComparer.Ordinal);

        /// <summary>The delivery states the server answers, by their wire names.</summary>
        private static readonly FrozenDictionary<string, IdentityDeliveryState> DeliveryStates =
            Enum.GetValues<IdentityDeliveryState>().ToFrozenDictionary(static state => state.ToString(), StringComparer.Ordinal);

        /// <summary>The token and the exchanges.</summary>
        private readonly IdentityTransport _transport = new(httpClients, () => options.Value, time);

        /// <summary>The call instruments.</summary>
        private readonly IdentityMetrics _metrics = new(meterFactory);

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentityInvitationResult>> InviteAsync(
            IReadOnlyList<IdentityInvitation> invitations, CancellationToken cancellationToken = default)
        {
            // --- The input: every invitation present and addressed ---
            ArgumentNullException.ThrowIfNull(invitations);
            foreach (IdentityInvitation? invitation in invitations)
            {
                if (invitation is null || string.IsNullOrWhiteSpace(invitation.Email))
                {
                    throw new ArgumentException("Every invitation is present and carries an email address.", nameof(invitations));
                }
            }

            return invitations.Count == 0
                ? []
                : await RunChunkedAsync(
                    UsersTelemetryNames.EndpointInvite,
                    UsersTelemetryNames.IdentityInviteActivity,
                    invitations,
                    InviteChunkAsync,
                    cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentityDeliveryStatus>> GetInvitationDeliveryAsync(
            IReadOnlyList<string> subjects, CancellationToken cancellationToken = default)
        {
            // --- The input: every subject present ---
            ArgumentNullException.ThrowIfNull(subjects);
            return subjects.Any(string.IsNullOrWhiteSpace)
                ? throw new ArgumentException("Every subject is a non-empty string.", nameof(subjects))
                : subjects.Count == 0
                ? []
                : await RunChunkedAsync(
                    UsersTelemetryNames.EndpointDelivery,
                    UsersTelemetryNames.IdentityDeliveryActivity,
                    subjects,
                    DeliveryChunkAsync,
                    cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IdentityServiceAccount> CreateServiceAccountAsync(string displayName, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            return await RunSingleAsync(
                UsersTelemetryNames.EndpointServiceAccount,
                async token =>
                {
                    // The audience of the distribution's bearer surface: its public origin.
                    string origin = options.Value.ServiceAccountResource
                        ?? throw new InvalidOperationException("Tellma:PublicOrigin is not set, so a service account has no audience to be issued for.");
                    CreateServiceAccountRequest body = new(displayName, [origin]);

                    CreateServiceAccountResponse response = (await _transport.SendAsync(
                        authority => Post(authority, ServiceAccountsRoute, body),
                        IdentityJson.TypeInfo<CreateServiceAccountResponse>(),
                        notFoundIsSuccess: false,
                        token).ConfigureAwait(false))!;

                    return string.IsNullOrWhiteSpace(response.ClientId) || string.IsNullOrWhiteSpace(response.ClientSecret)
                        ? throw Unreadable("The identity server created a service account but answered no client id or no secret.")
                        : new IdentityServiceAccount(response.ClientId, response.ClientSecret);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteServiceAccountAsync(string clientId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

            _ = await RunSingleAsync(
                UsersTelemetryNames.EndpointServiceAccount,
                async token =>
                {
                    // A 404 is success: the server scopes the delete to this distribution's accounts,
                    // and one it does not find no longer exists for it.
                    Uri route = new(ServiceAccountsRoute + "/" + Uri.EscapeDataString(clientId), UriKind.Relative);
                    _ = await _transport.SendAsync<CreateServiceAccountResponse>(
                        authority => new HttpRequestMessage(HttpMethod.Delete, new Uri(authority, route)),
                        responseType: null,
                        notFoundIsSuccess: true,
                        token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------------------------------
        // The chunks
        // ------------------------------------------------------------------------------------------

        /// <summary>Sends one chunk of the bulk invite and reads its results.</summary>
        /// <param name="chunk">The chunk's invitations.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>One result per invitation, in order.</returns>
        private async Task<IReadOnlyList<IdentityInvitationResult>> InviteChunkAsync(
            IReadOnlyList<IdentityInvitation> chunk, CancellationToken cancellationToken)
        {
            InviteUsersRequest body = new([.. chunk.Select(static invitation => new InviteUserItem(
                invitation.Email,
                invitation.DisplayName,
                invitation.Locale,
                invitation.Gender,
                invitation.ReturnUrl,
                invitation.ExistingOnly))]);

            InviteUsersResponse response = (await _transport.SendAsync(
                authority => Post(authority, InviteRoute, body),
                IdentityJson.TypeInfo<InviteUsersResponse>(),
                notFoundIsSuccess: false,
                cancellationToken).ConfigureAwait(false))!;

            // --- One result per invitation, each in its invitation's position ---
            // A misplaced result would record one user's subject on another user's row, so the
            // echoed address is checked, not only the count.
            IReadOnlyList<InviteUserResult> results = response.Results is { } answered && answered.Count == chunk.Count
                ? answered
                : throw Unreadable($"The identity server answered {response.Results?.Count ?? 0} invitation result(s) for {chunk.Count} invitation(s).");

            List<IdentityInvitationResult> mapped = new(chunk.Count);
            for (int i = 0; i < chunk.Count; i++)
            {
                IdentityInvitation invitation = chunk[i];
                InviteUserResult result = results[i];
                if (!string.Equals(result.Email, invitation.Email, StringComparison.Ordinal))
                {
                    throw Unreadable($"The identity server's invitation result at position {i} names another address than the invitation in that position.");
                }

                // Either a status with its subject, or a refusal: anything else cannot be recorded.
                InviteStatus? status = result.Status is null
                    ? null
                    : InviteStatuses.TryGetValue(result.Status, out InviteStatus known)
                    ? known
                    : throw Unreadable($"The identity server's invitation result at position {i} carries a status the client does not know.");
                bool sound = status is null
                    ? !string.IsNullOrWhiteSpace(result.Error)
                    : !string.IsNullOrWhiteSpace(result.Sub) && result.Error is null;
                mapped.Add(sound
                    ? new IdentityInvitationResult(invitation.Email, result.Sub, status, result.Error)
                    : throw Unreadable($"The identity server's invitation result at position {i} is neither a status with a subject nor a refusal."));
            }

            return mapped;
        }

        /// <summary>Sends one chunk of the bulk delivery status and reads its results.</summary>
        /// <param name="chunk">The chunk's subjects.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>One status per subject, in order.</returns>
        private async Task<IReadOnlyList<IdentityDeliveryStatus>> DeliveryChunkAsync(
            IReadOnlyList<string> chunk, CancellationToken cancellationToken)
        {
            DeliveryStatusRequest body = new(chunk);
            DeliveryStatusResponse response = (await _transport.SendAsync(
                authority => Post(authority, DeliveryRoute, body),
                IdentityJson.TypeInfo<DeliveryStatusResponse>(),
                notFoundIsSuccess: false,
                cancellationToken).ConfigureAwait(false))!;

            // --- One status per subject, each echoing its subject ---
            IReadOnlyList<DeliveryStatusResult> results = response.Results is { } answered && answered.Count == chunk.Count
                ? answered
                : throw Unreadable($"The identity server answered {response.Results?.Count ?? 0} delivery status(es) for {chunk.Count} subject(s).");

            List<IdentityDeliveryStatus> mapped = new(chunk.Count);
            for (int i = 0; i < chunk.Count; i++)
            {
                DeliveryStatusResult result = results[i];
                mapped.Add(!string.Equals(result.Sub, chunk[i], StringComparison.Ordinal)
                    ? throw Unreadable($"The identity server's delivery status at position {i} names another subject than the one asked in that position.")
                    : result.State is null || !DeliveryStates.TryGetValue(result.State, out IdentityDeliveryState state)
                    ? throw Unreadable($"The identity server's delivery status at position {i} carries a state the client does not know.")
                    : new IdentityDeliveryStatus(chunk[i], state, result.ExpectsDeliveryEvents, result.SentUtc, result.UpdatedUtc, result.Reason));
            }

            return mapped;
        }

        // ------------------------------------------------------------------------------------------
        // Calls: chunking, the prefix, and the telemetry
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Runs a bulk call chunk by chunk, in order. Before any chunk completed, a failure raises;
        ///     after one, any failure or a cancellation answers the completed chunks' results.
        /// </summary>
        /// <typeparam name="TItem">An input item.</typeparam>
        /// <typeparam name="TResult">A result.</typeparam>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="activityName">The name of each chunk's activity.</param>
        /// <param name="items">The input, at least one item.</param>
        /// <param name="send">Sends one chunk and answers its results.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The results, or their prefix.</returns>
        private async Task<IReadOnlyList<TResult>> RunChunkedAsync<TItem, TResult>(
            string endpoint,
            string activityName,
            IReadOnlyList<TItem> items,
            Func<IReadOnlyList<TItem>, CancellationToken, Task<IReadOnlyList<TResult>>> send,
            CancellationToken cancellationToken)
        {
            long started = time.GetTimestamp();
            string outcome = UsersTelemetryNames.OutcomeOk;
            List<TResult> results = new(items.Count);
            try
            {
                for (int offset = 0; offset < items.Count; offset += IIdentityServerClient.MaxChunkSize)
                {
                    // --- A cancellation between chunks: the prefix, or the cancellation itself before any ---
                    if (cancellationToken.IsCancellationRequested && results.Count > 0)
                    {
                        outcome = UsersTelemetryNames.OutcomePartial;
                        return results;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // --- One chunk, one HTTP call, one activity ---
                    IReadOnlyList<TItem> chunk = [.. items.Skip(offset).Take(IIdentityServerClient.MaxChunkSize)];
                    using Activity? activity = DataExecutor.ActivitySource.StartActivity(activityName, ActivityKind.Internal);
                    _ = activity?.SetTag(UsersTelemetryNames.IdentityCountTag, chunk.Count);
                    try
                    {
                        results.AddRange(await send(chunk, cancellationToken).ConfigureAwait(false));
                        _ = activity?.SetTag(UsersTelemetryNames.IdentityOutcomeTag, UsersTelemetryNames.OutcomeOk);
                    }
                    catch (Exception exception) when (results.Count > 0)
                    {
                        // Earlier chunks completed on the server and their subjects are real, so they
                        // are answered whatever stopped this one; the caller re-runs the rest.
                        _ = activity?.SetTag(UsersTelemetryNames.IdentityOutcomeTag, OutcomeOf(exception, cancellationToken));
                        IdentityLog.ChunkFailed(logger, exception, endpoint, results.Count, items.Count - results.Count);
                        outcome = UsersTelemetryNames.OutcomePartial;
                        return results;
                    }
                    catch (Exception exception)
                    {
                        _ = activity?.SetTag(UsersTelemetryNames.IdentityOutcomeTag, OutcomeOf(exception, cancellationToken));
                        throw;
                    }
                }

                return results;
            }
            catch (Exception exception)
            {
                outcome = OutcomeOf(exception, cancellationToken);
                throw;
            }
            finally
            {
                Record(endpoint, items.Count, outcome, started);
            }
        }

        /// <summary>Runs a single-call member, counted, timed and logged once.</summary>
        /// <typeparam name="T">What the call yields.</typeparam>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="call">The call.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>What the call yields.</returns>
        private async Task<T> RunSingleAsync<T>(string endpoint, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
        {
            long started = time.GetTimestamp();
            string outcome = UsersTelemetryNames.OutcomeOk;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                outcome = OutcomeOf(exception, cancellationToken);
                throw;
            }
            finally
            {
                Record(endpoint, 1, outcome, started);
            }
        }

        /// <summary>Counts, times and logs one call.</summary>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="count">How many items the call was asked to carry.</param>
        /// <param name="outcome">The outcome's tag value.</param>
        /// <param name="started">The call's start timestamp on <see cref="TimeProvider" />.</param>
        private void Record(string endpoint, int count, string outcome, long started)
        {
            TimeSpan elapsed = time.GetElapsedTime(started);
            _metrics.RecordCall(endpoint, outcome, elapsed);
            IdentityLog.IdentityCall(logger, endpoint, count, outcome, (long)elapsed.TotalMilliseconds);
        }

        /// <summary>The outcome tag value of a failed call or chunk.</summary>
        /// <param name="exception">What stopped it.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The value.</returns>
        private static string OutcomeOf(Exception exception, CancellationToken cancellationToken)
        {
            return exception switch
            {
                DependencyUnavailableException => UsersTelemetryNames.OutcomeUnavailable,
                OperationCanceledException when cancellationToken.IsCancellationRequested => UsersTelemetryNames.OutcomeCancelled,
                _ => UsersTelemetryNames.OutcomeError,
            };
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>A JSON POST to a route under the authority.</summary>
        /// <typeparam name="T">The body's shape.</typeparam>
        /// <param name="authority">The authority, with a trailing slash.</param>
        /// <param name="route">The route, relative to the authority.</param>
        /// <param name="body">The body.</param>
        /// <returns>The request.</returns>
        private static HttpRequestMessage Post<T>(Uri authority, string route, T body)
        {
            JsonTypeInfo<T> type = IdentityJson.TypeInfo<T>();
            return new HttpRequestMessage(HttpMethod.Post, new Uri(authority, route)) { Content = JsonContent.Create(body, type) };
        }

        /// <summary>The failure of an answer the client cannot record.</summary>
        /// <param name="message">What is wrong with it; never an address, a subject or a secret.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException Unreadable(string message)
        {
            return new InvalidOperationException(message);
        }
    }
}
