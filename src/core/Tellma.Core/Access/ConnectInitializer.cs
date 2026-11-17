// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Access
{
    // The initializer is spec 0014 §7.8, at Order 100: before spec 0013's negotiation at 200, which
    // reads the connected user's stored preferences.
    /// <summary>
    ///     Connects a tenant scope's principal to its user: a request's caller by subject, a background
    ///     scope that runs as a user by id, and the platform's own work as the system user. The
    ///     completed context names the user, and a connected user or service account is recorded as
    ///     the scope's member witness.
    /// </summary>
    /// <param name="connector">The scope's user connector.</param>
    /// <param name="witness">The scope's access witness.</param>
    internal sealed class ConnectInitializer(UserConnector connector, AccessWitness witness) : IRequestContextInitializer
    {
        /// <summary>The initializer's order: the first of the platform's.</summary>
        public const int ConnectOrder = 100;

        /// <inheritdoc />
        public int Order => ConnectOrder;

        /// <inheritdoc />
        public async Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(inputs);

            // Tenantless work connects no one.
            if (context.Tenant is not TenantDescriptor tenant)
            {
                return context;
            }

            // --- What the request asked for: the activity stamp's suppression, and the inputs a re-run of the later initializers needs ---
            connector.Inputs = inputs;
            switch (context.Kind)
            {
                case PrincipalKind.System:
                    _ = await connector.ConnectAsSystemAsync(cancellationToken).ConfigureAwait(false);
                    return context with { UserId = context.UserId ?? WellKnownIds.SystemUserId };

                case PrincipalKind.User or PrincipalKind.ServiceAccount:
                    // A background scope carries its run-as user's id; a request carries the subject alone.
                    ConnectedUser user = context.UserId is int userId
                        ? await connector.ConnectAsUserAsync(userId, cancellationToken).ConfigureAwait(false)
                        : await connector.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    // A connected member satisfies a member endpoint's witness (spec 0014 §4.6).
                    witness.RecordMember();
                    return context with { UserId = user.UserId };

                case PrincipalKind.Anonymous:
                    // Nobody authenticated: the tenant is not found, as it is for a stranger.
                    throw new TenantNotFoundException(tenant.Id);

                default:
                    throw new InvalidOperationException($"The principal kind {context.Kind} is unknown to the user connection.");
            }
        }
    }
}
