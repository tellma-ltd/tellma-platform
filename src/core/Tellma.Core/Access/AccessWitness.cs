// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Access
{
    // The witness is spec 0014 §4.6's layer 4: the evaluator records every evaluated pair, the connect
    // initializer the member witness (§7.8), and the host's witness filter confirms before a 2xx.
    /// <summary>
    ///     What the scope evaluated for its caller: every pair the evaluator decided through
    ///     <see cref="IAccessEvaluator.EvaluateAsync(string, string, CancellationToken)" /> or
    ///     <see cref="IAccessEvaluator.RequireAsync" />, and whether the caller connected as a member.
    ///     One per scope; safe for concurrent use.
    /// </summary>
    /// <param name="accessor">The scope's request context, for the refusal's log.</param>
    /// <param name="registry">The securable registry, which resolves an endpoint's pair to the registered one.</param>
    /// <param name="metrics">The access instruments.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    internal sealed class AccessWitness(
        IRequestContextAccessor accessor,
        ISecurableRegistry registry,
        AccessMetrics metrics,
        ILoggerFactory loggerFactory)
    {
        /// <summary>The value logged as expected for a member endpoint.</summary>
        private const string MemberExpectation = "member";

        /// <summary>Guards the recorded state.</summary>
        private readonly Lock _gate = new();

        /// <summary>The pairs evaluated, ignoring case.</summary>
        private readonly HashSet<SecurableRef> _evaluated = new(PairComparer.Instance);

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <summary>Whether the caller connected as a member: a user or a service account.</summary>
        public bool IsMemberWitnessed
        {
            get
            {
                lock (_gate)
                {
                    return field;
                }
            }

            private set
            {
                lock (_gate)
                {
                    field = value;
                }
            }
        }

        /// <summary>The pairs evaluated so far, in no particular order.</summary>
        public IReadOnlyList<SecurableRef> Evaluated
        {
            get
            {
                lock (_gate)
                {
                    return [.. _evaluated];
                }
            }
        }

        /// <summary>Records that a pair was evaluated for the caller.</summary>
        /// <param name="resource">The pair's resource, as registered.</param>
        /// <param name="action">The pair's action, as registered.</param>
        public void Record(string resource, string action)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            lock (_gate)
            {
                _ = _evaluated.Add(new SecurableRef(resource, action));
            }
        }

        /// <summary>Records that the caller connected as a member.</summary>
        public void RecordMember()
        {
            IsMemberWitnessed = true;
        }

        /// <summary>Whether a pair was evaluated for the caller, through an alias or another spelling included.</summary>
        /// <param name="resource">The pair's resource.</param>
        /// <param name="action">The pair's action.</param>
        /// <returns>True when it was.</returns>
        public bool HasEvaluated(string resource, string action)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);

            // The evaluator records the registered pair, so an endpoint naming a former name resolves first.
            SecurableRef wanted = registry.Find(resource, action) is SecurableDescriptor registered
                ? new SecurableRef(registered.Resource, registered.Action)
                : new SecurableRef(resource, action);
            lock (_gate)
            {
                return _evaluated.Contains(wanted);
            }
        }

        /// <summary>
        ///     Confirms, before a successful response is written, that the endpoint's declared
        ///     securable was evaluated, or for a member endpoint that the caller connected as a
        ///     member. A response without its witness is counted and logged, for the caller to
        ///     replace with an error.
        /// </summary>
        /// <param name="securable">The endpoint's declared securable; null for a member endpoint.</param>
        /// <param name="endpoint">The endpoint's display name, for the log.</param>
        /// <returns>True when the witness is present.</returns>
        public bool Confirm(SecurableEndpointMetadata? securable, string endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            bool witnessed = securable is null ? IsMemberWitnessed : HasEvaluated(securable.Resource, securable.Action);
            if (witnessed)
            {
                return true;
            }

            // --- No witness: counted and logged at Error; the caller answers with an error ---
            RequestContext context = accessor.Current;
            metrics.CountMissingWitness();
            AccessLog.MissingWitness(
                _logger,
                context.Tenant?.Id ?? 0,
                context.UserId,
                endpoint,
                securable is null ? MemberExpectation : $"{securable.Resource}:{securable.Action}");
            return false;
        }

        /// <summary>Compares pairs by resource and action, ignoring case, as the registry does.</summary>
        private sealed class PairComparer : IEqualityComparer<SecurableRef>
        {
            /// <summary>The comparer.</summary>
            public static PairComparer Instance { get; } = new();

            /// <inheritdoc />
            public bool Equals(SecurableRef? x, SecurableRef? y)
            {
                return ReferenceEquals(x, y)
                    || (x is not null && y is not null
                        && string.Equals(x.Resource, y.Resource, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Action, y.Action, StringComparison.OrdinalIgnoreCase));
            }

            /// <inheritdoc />
            public int GetHashCode(SecurableRef obj)
            {
                ArgumentNullException.ThrowIfNull(obj);
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Resource),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Action));
            }
        }
    }
}
