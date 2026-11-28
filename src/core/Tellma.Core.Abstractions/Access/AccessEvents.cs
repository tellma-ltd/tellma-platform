// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The access log events: their logger category and event ids, which alert queries key on, so
    ///     they never change. Tenant, user, permission and role ids are the events' structured
    ///     properties.
    /// </summary>
    public static class AccessEvents
    {
        /// <summary>The logger category every access event is written under.</summary>
        public const string Category = "Tellma.Core.Access";

        /// <summary>Error: a successful response carried no access witness.</summary>
        public const int MissingWitness = 3100;

        /// <summary>Warning: a stored permission grants nothing; once per permission and problem per version of the tenant's permissions.</summary>
        public const int PermissionDrift = 3101;

        /// <summary>Warning: a guarded batch found its premises stale twice in a row.</summary>
        public const int GuardThrash = 3102;

        /// <summary>Warning: a security guard refused a write.</summary>
        public const int GuardTriggered = 3103;

        /// <summary>Information: a role save tried to grant beyond what its author holds.</summary>
        public const int EscalationRejected = 3104;

        /// <summary>Information: a connect was refused, for an unknown subject or a deactivated user.</summary>
        public const int ConnectRejected = 3105;

        /// <summary>Debug: a caller's permission set was built.</summary>
        public const int AccessSetBuilt = 3106;

        /// <summary>Error: a composed access filter exceeded the compile ceilings.</summary>
        public const int FilterTooComplex = 3107;
    }
}
