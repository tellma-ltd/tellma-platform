// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     What a request asked for about its own locale and client, handed unchanged to every
    ///     <see cref="IRequestContextInitializer" />. Every member is the raw text the surface
    ///     received, or null when the request did not say.
    /// </summary>
    /// <remarks>
    ///     The web surface fills the first four from the request's headers and passes neither message
    ///     member. A surface whose text is read by an agent rather than a person passes the two
    ///     message members: <paramref name="MessageLanguage" />, when the deployment offers it, is the
    ///     message language and formatting culture, and <paramref name="MessageCalendar" /> is the
    ///     formatting calendar even when the tenant does not use it. A background scope passes
    ///     <see cref="Empty" />.
    /// </remarks>
    /// <param name="AcceptLanguage">The languages the caller accepts, in the <c>Accept-Language</c> grammar.</param>
    /// <param name="RequestedCalendar">The calendar code the caller asked for.</param>
    /// <param name="RequestedTimeZone">The display zone the caller asked for, an IANA name.</param>
    /// <param name="Client">The client the caller says it is.</param>
    /// <param name="MessageLanguage">The language the surface writes its messages in, overriding the negotiation.</param>
    /// <param name="MessageCalendar">The calendar the surface formats its messages in, overriding the negotiation.</param>
    public sealed record RequestContextInputs(
        string? AcceptLanguage,
        string? RequestedCalendar,
        string? RequestedTimeZone,
        string? Client,
        string? MessageLanguage,
        string? MessageCalendar)
    {
        /// <summary>The inputs of work no request asked for: every member null.</summary>
        public static RequestContextInputs Empty { get; } = new(null, null, null, null, null, null);
    }
}
