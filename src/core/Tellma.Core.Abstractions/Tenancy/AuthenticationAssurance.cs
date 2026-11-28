// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     How strongly, and how recently, the caller's session was authenticated, as the authority
    ///     asserted it. An endpoint that demands an assurance level compares its bar
    ///     (<see cref="RequireAssuranceMetadata" />) against these values.
    /// </summary>
    /// <param name="Acr">The authentication context class the session reached; null when the authority asserted none.</param>
    /// <param name="AuthTime">When the user last signed in; null when the authority did not say.</param>
    /// <param name="AcrAuthTime">
    ///     When the session last reached <paramref name="Acr" />, which a step-up moves without a new
    ///     sign-in; null when the authority did not say, in which case <paramref name="AuthTime" />
    ///     stands in for it.
    /// </param>
    public sealed record AuthenticationAssurance(string? Acr, DateTimeOffset? AuthTime, DateTimeOffset? AcrAuthTime);
}
