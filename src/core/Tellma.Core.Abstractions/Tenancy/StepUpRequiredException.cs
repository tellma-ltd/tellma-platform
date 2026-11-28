// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The operation demands a stronger or a more recent authentication than the caller's session
    ///     holds, and the caller can reach it by signing in again. Maps to 401 with the problem code
    ///     <c>step-up-required</c> and a challenge naming the bar, so the client sends the user
    ///     through a step-up and retries.
    /// </summary>
    /// <remarks>
    ///     Raised for an endpoint that carries an assurance bar, and by an access check a service
    ///     makes in code for a sensitive securable, under the same bar. A service account never
    ///     receives it: it has no sign-in to step up, so it is refused as not an individual instead.
    /// </remarks>
    /// <param name="acr">The authentication context class the session must reach.</param>
    /// <param name="maxAge">How recently the session must have reached it; null when any age will do.</param>
    /// <exception cref="ArgumentException"><paramref name="acr" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge" /> is zero or negative.</exception>
    public sealed class StepUpRequiredException(string acr, TimeSpan? maxAge)
        : TellmaException(
            ProblemCode,
            $"The operation requires the session to reach the authentication context class '{RequireAssuranceMetadata.ValidAcr(acr)}'; the caller must step up.")
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "step-up-required";

        /// <summary>The authentication context class the session must reach.</summary>
        public string Acr { get; } = acr;

        /// <summary>How recently the session must have reached <see cref="Acr" />; null when any age will do.</summary>
        public TimeSpan? MaxAge { get; } = RequireAssuranceMetadata.ValidMaxAge(maxAge);
    }
}
