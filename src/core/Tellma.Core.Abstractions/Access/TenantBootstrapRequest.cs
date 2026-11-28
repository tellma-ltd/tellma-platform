// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The request is spec 0014 §11.
    /// <summary>The first individual administrator a tenant's provisioning names.</summary>
    /// <param name="Email">The administrator's email address; normalised (trimmed, lower-cased) before it is stored or looked up.</param>
    /// <param name="Name">The administrator's display name, in the tenant's primary language.</param>
    /// <param name="PreferredLanguage">The administrator's preferred language, a bare code; null for the tenant's primary language.</param>
    /// <param name="Subject">
    ///     The administrator's identity-provider subject, in a development environment only, where
    ///     the first sign-in then binds the row with no invitation; null elsewhere.
    /// </param>
    public sealed record TenantBootstrapRequest(string Email, string Name, string? PreferredLanguage, string? Subject);
}
