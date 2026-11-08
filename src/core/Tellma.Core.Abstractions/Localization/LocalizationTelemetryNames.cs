// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The names of the localization instruments and their tags, on the platform's
    ///     <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so they never
    ///     change. No tag carries a tenant, a user or a resource key: those are structured log
    ///     properties.
    /// </summary>
    public static class LocalizationTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>
        ///     Counter (<c>{lookup}</c>): a resource key absent from every culture of the fallback
        ///     chain, counted once per key per culture per process. Tagged <see cref="CultureTag" />.
        /// </summary>
        public const string Missing = "tellma.localization.missing";

        /// <summary>
        ///     Counter (<c>{header}</c>): a locale request header that was present but invalid and was
        ///     ignored. Tagged <see cref="HeaderTag" />.
        /// </summary>
        public const string HeadersRejected = "tellma.localization.headers.rejected";

        /// <summary>The culture tag: an offered language code.</summary>
        public const string CultureTag = "culture";

        /// <summary>The header tag: <c>accept-language</c>, <c>calendar</c> or <c>time-zone</c>.</summary>
        public const string HeaderTag = "header";
    }
}
