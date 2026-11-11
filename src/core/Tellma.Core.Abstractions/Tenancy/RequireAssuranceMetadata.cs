// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Endpoint metadata: the assurance bar a caller's session must meet before the endpoint runs.
    ///     A session below the bar is asked to step up (<see cref="StepUpRequiredException" />); a
    ///     service account, which has no interactive sign-in to step up, is refused.
    /// </summary>
    /// <param name="Acr">The authentication context class the session must have reached.</param>
    /// <param name="MaxAge">How recently the session must have reached it; null when any age will do.</param>
    /// <exception cref="ArgumentException"><paramref name="Acr" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="MaxAge" /> is zero or negative.</exception>
    public sealed record RequireAssuranceMetadata(string Acr, TimeSpan? MaxAge)
    {
        /// <inheritdoc cref="RequireAssuranceMetadata(string, TimeSpan?)" path="/param[@name='Acr']" />
        public string Acr
        {
            get;
            init => field = ValidAcr(value);
        } = ValidAcr(Acr);

        /// <inheritdoc cref="RequireAssuranceMetadata(string, TimeSpan?)" path="/param[@name='MaxAge']" />
        public TimeSpan? MaxAge
        {
            get;
            init => field = ValidMaxAge(value);
        } = ValidMaxAge(MaxAge);

        /// <summary>Refuses a blank class.</summary>
        /// <param name="acr">The class.</param>
        /// <returns>The class.</returns>
        internal static string ValidAcr(string acr)
        {
            return string.IsNullOrWhiteSpace(acr)
                ? throw new ArgumentException("An assurance bar names the authentication context class it requires.", nameof(acr))
                : acr;
        }

        /// <summary>Refuses an age that no session can meet.</summary>
        /// <param name="maxAge">The age.</param>
        /// <returns>The age.</returns>
        internal static TimeSpan? ValidMaxAge(TimeSpan? maxAge)
        {
            return maxAge is not { } value || value > TimeSpan.Zero
                ? maxAge
                : throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "An assurance bar's maximum age is positive.");
        }
    }
}
