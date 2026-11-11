// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Globalization;

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     Writes the step-up challenge: <c>401</c> with
    ///     <c>WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…</c>,
    ///     the RFC 9470 answer to a session that falls short of an operation's assurance bar.
    /// </summary>
    public static class StepUpChallenge
    {
        /// <summary>The RFC 9470 error code.</summary>
        public const string Error = "insufficient_user_authentication";

        /// <summary>Sets the challenge's status and header on a response that has not started.</summary>
        /// <param name="response">The response.</param>
        /// <param name="acrValues">The authentication context class the caller must reach.</param>
        /// <param name="maxAge">How recently, in seconds, it must have been reached; null for no limit.</param>
        /// <exception cref="ArgumentException">
        ///     <paramref name="acrValues" /> is empty or would break out of the quoted header value.
        /// </exception>
        public static void Write(HttpResponse response, string acrValues, int? maxAge)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentException.ThrowIfNullOrWhiteSpace(acrValues);

            // The value is quoted in the header, so a quote, a backslash or a control character
            // would end or corrupt it.
            if (acrValues.Any(static c => c is '"' or '\\' || char.IsControl(c)))
            {
                throw new ArgumentException("An acr value may not carry a quote, a backslash or a control character.", nameof(acrValues));
            }

            if (maxAge is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "A maximum authentication age is zero or positive.");
            }

            string challenge = maxAge is { } seconds
                ? string.Create(CultureInfo.InvariantCulture, $"Bearer error=\"{Error}\", acr_values=\"{acrValues}\", max_age={seconds}")
                : $"Bearer error=\"{Error}\", acr_values=\"{acrValues}\"";

            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers[HeaderNames.WWWAuthenticate] = challenge;
        }
    }
}
