// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Identity;

namespace Tellma.Testing.Support.Identity
{
    /// <summary>
    ///     An identity-server client that answers from a script and records every call, so a suite can
    ///     drive the users' stack without an identity server: by default each invitation answers
    ///     <see cref="InviteStatus.Invited" /> with a subject derived from its address, each service
    ///     account is created with a client id unique to this client and a fresh secret, and each delivery answers
    ///     <see cref="IdentityDeliveryState.Sent" />.
    /// </summary>
    /// <remarks>Thread-safe: the recorded calls are concurrent collections.</remarks>
    public sealed class ScriptedIdentityClient : IIdentityServerClient
    {
        /// <summary>This client's own prefix of the client ids it creates, so two clients over one database never collide.</summary>
        private readonly string _prefix = "svc-" + Guid.NewGuid().ToString("N")[..8] + "-";

        /// <summary>The number of service accounts created, which numbers the next one.</summary>
        private int _created;

        /// <summary>
        ///     Answers one invite call; null for the default, one <see cref="InviteStatus.Invited" />
        ///     result per invitation with the subject <see cref="SubjectOf" /> derives.
        /// </summary>
        public Func<IReadOnlyList<IdentityInvitation>, CancellationToken, Task<IReadOnlyList<IdentityInvitationResult>>>? OnInvite { get; set; }

        /// <summary>Runs before a service account is created, such as a step that commits a concurrent write; null for nothing.</summary>
        public Func<string, CancellationToken, Task>? BeforeCreate { get; set; }

        /// <summary>Answers one delivery call; null for the default, <see cref="IdentityDeliveryState.Sent" /> per subject.</summary>
        public Func<IReadOnlyList<string>, IReadOnlyList<IdentityDeliveryStatus>>? OnDelivery { get; set; }

        /// <summary>Fails a service account's deletion when set; null deletes every one.</summary>
        public Exception? DeleteFailure { get; set; }

        /// <summary>Every invite call's invitations, in call order.</summary>
        public ConcurrentQueue<IReadOnlyList<IdentityInvitation>> Invites { get; } = new();

        /// <summary>Every delivery call's subjects, in call order.</summary>
        public ConcurrentQueue<IReadOnlyList<string>> Deliveries { get; } = new();

        /// <summary>Every service account created, by display name, in call order.</summary>
        public ConcurrentQueue<(string DisplayName, IdentityServiceAccount Account)> Created { get; } = new();

        /// <summary>Every client id asked to be deleted, in call order.</summary>
        public ConcurrentQueue<string> Deleted { get; } = new();

        /// <summary>The subject the default script answers for an address: <c>sub-</c> and the address, lower-cased.</summary>
        /// <param name="email">The address.</param>
        /// <returns>The subject.</returns>
        public static string SubjectOf(string email)
        {
            ArgumentNullException.ThrowIfNull(email);
            return "sub-" + email.ToLowerInvariant();
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IdentityInvitationResult>> InviteAsync(IReadOnlyList<IdentityInvitation> invitations, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(invitations);
            IdentityInvitation[] sent = [.. invitations];
            Invites.Enqueue(sent);
            return OnInvite is { } script
                ? await script(sent, cancellationToken).ConfigureAwait(false)
                : [.. sent.Select(static invitation => new IdentityInvitationResult(invitation.Email, SubjectOf(invitation.Email), InviteStatus.Invited, null))];
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<IdentityDeliveryStatus>> GetInvitationDeliveryAsync(IReadOnlyList<string> subjects, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subjects);
            string[] asked = [.. subjects];
            Deliveries.Enqueue(asked);
            IReadOnlyList<IdentityDeliveryStatus> answered = OnDelivery is { } script
                ? script(asked)
                : [.. asked.Select(static subject => new IdentityDeliveryStatus(subject, IdentityDeliveryState.Sent, false, DateTimeOffset.UnixEpoch, null, null))];
            return Task.FromResult(answered);
        }

        /// <inheritdoc />
        public async Task<IdentityServiceAccount> CreateServiceAccountAsync(string displayName, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            if (BeforeCreate is { } before)
            {
                await before(displayName, cancellationToken).ConfigureAwait(false);
            }

            int number = Interlocked.Increment(ref _created);
            IdentityServiceAccount account = new(
                string.Create(CultureInfo.InvariantCulture, $"{_prefix}{number}"),
                string.Create(CultureInfo.InvariantCulture, $"secret-{number}-{Guid.NewGuid():N}"));
            Created.Enqueue((displayName, account));
            return account;
        }

        /// <inheritdoc />
        public Task DeleteServiceAccountAsync(string clientId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            Deleted.Enqueue(clientId);
            return DeleteFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }
}
