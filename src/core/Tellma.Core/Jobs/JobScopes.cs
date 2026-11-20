// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §8: the job scope's snapshot names the tenant, the run-as user and the worker
    // client; the system user's scope is the platform's own. The trace parent is the job's when
    // the partition holds one job: correlation data for the log, never a parent or a link.
    /// <summary>The snapshots of the scopes the job machinery opens.</summary>
    internal static class JobScopes
    {
        /// <summary>The client every job scope runs as: one of the platform's clients that never reaches a request filter.</summary>
        public const string Client = "worker";

        /// <summary>The subject of the system user's scope.</summary>
        public const string SystemSubject = "system";

        /// <summary>The system scope a poll, a renewal and a completion without a handler run in.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The snapshot.</returns>
        public static RequestContextSnapshot System(int tenantId)
        {
            return new RequestContextSnapshot(tenantId, PrincipalKind.System, SystemSubject, WellKnownIds.SystemUserId, Client, TraceParent: null);
        }

        /// <summary>
        ///     The scope a partition's handler runs in: the system scope for the system user, else a
        ///     user scope, which the connect initializer binds by the user's id, so it names no subject.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="runAsUserId">The partition's run-as user.</param>
        /// <param name="traceParent">The job's trace parent when the partition holds one job; null otherwise.</param>
        /// <returns>The snapshot.</returns>
        public static RequestContextSnapshot RunAs(int tenantId, int runAsUserId, string? traceParent)
        {
            return runAsUserId == WellKnownIds.SystemUserId
                ? System(tenantId) with { TraceParent = traceParent }
                : new RequestContextSnapshot(tenantId, PrincipalKind.User, Subject: null, runAsUserId, Client, traceParent);
        }
    }
}
