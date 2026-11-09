// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     Decides one reader's culture, calendar, display zone and content language. Each value takes
    ///     the first valid of: a surface's override, the request header, the stored preference, the
    ///     tenant's setting, the distribution default.
    /// </summary>
    /// <remarks>
    ///     An invalid header (an unknown calendar, an unparseable language range, an unknown zone) is
    ///     ignored and counted, never refused: a stale browser must not lock its user out.
    /// </remarks>
    public interface ILocalizationNegotiator
    {
        /// <summary>Negotiates. Never throws for any input value.</summary>
        /// <param name="input">What to weigh.</param>
        /// <returns>The negotiated context.</returns>
        LocalizationContext Negotiate(NegotiationInput input);
    }
}
