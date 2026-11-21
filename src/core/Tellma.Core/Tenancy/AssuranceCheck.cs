// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    // The bar is spec 0011's; the access guard applies an endpoint's, the evaluator a sensitive securable's.
    /// <summary>Judges a caller's session against an assurance bar.</summary>
    internal static class AssuranceCheck
    {
        /// <summary>Refuses a session below a bar.</summary>
        /// <param name="context">The completed context, whose principal and assurance are judged.</param>
        /// <param name="bar">The bar.</param>
        /// <param name="now">The instant the session's age is judged at.</param>
        /// <exception cref="ArgumentNullException">The context or the bar is null.</exception>
        /// <exception cref="IndividualRequiredException">The caller is a service account.</exception>
        /// <exception cref="StepUpRequiredException">The session did not reach the bar's class or a higher tier, or not recently enough.</exception>
        public static void Ensure(RequestContext context, RequireAssuranceMetadata bar, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(bar);

            if (context.Kind == PrincipalKind.ServiceAccount)
            {
                // A service account has no interactive sign-in to step up.
                throw new IndividualRequiredException();
            }

            // A session above the bar's tier reaches it: a device-bound passkey's aal3 meets an aal2 bar.
            AuthenticationAssurance? held = context.Assurance;
            bool reachedClass = AssuranceTiers.Satisfies(held?.Acr, bar.Acr);

            // The age counts from when the session reached the class, else from the sign-in; a
            // session that says neither, such as a background scope's, cannot prove it is recent enough.
            bool recentEnough = bar.MaxAge is not { } maxAge
                || ((held?.AcrAuthTime ?? held?.AuthTime) is { } reachedAt && now - reachedAt <= maxAge);

            if (!reachedClass || !recentEnough)
            {
                throw new StepUpRequiredException(bar.Acr, bar.MaxAge);
            }
        }
    }
}
