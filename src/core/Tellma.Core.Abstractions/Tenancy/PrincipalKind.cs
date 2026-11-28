// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     What kind of principal a unit of work runs for. The names are part of the contract: a
    ///     background caller stores one with the work it enqueues.
    /// </summary>
    public enum PrincipalKind
    {
        /// <summary>No authenticated principal: tenantless work, and a context nothing has bound yet.</summary>
        Anonymous,

        /// <summary>An individual who signed in interactively.</summary>
        User,

        /// <summary>A service account: a client acting for itself, with no interactive sign-in to step up.</summary>
        ServiceAccount,

        /// <summary>The platform itself: a provisioning step or other background work no user asked for.</summary>
        System,
    }
}
