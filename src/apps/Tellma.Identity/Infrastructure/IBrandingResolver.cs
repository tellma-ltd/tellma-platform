// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Infrastructure
{
    /// <summary>The branding tokens a page renders with.</summary>
    /// <param name="ProductName">The product name shown in chrome and email.</param>
    /// <param name="LogoPath">An app-relative logo path, when a logo exists.</param>
    /// <param name="WordmarkPath">
    ///     An app-relative wordmark for light chrome. Null renders the product name as text, so a
    ///     deployment that has not supplied artwork still shows a brand.
    /// </param>
    /// <param name="WordmarkOnDarkPath">
    ///     The wordmark for the dark brand panel. A two-color mark cannot inherit
    ///     <c>currentColor</c>, so light and dark are separate files rather than one recolored.
    /// </param>
    public sealed record BrandingInfo(
        string ProductName,
        string? LogoPath,
        string? WordmarkPath = null,
        string? WordmarkOnDarkPath = null);

    /// <summary>
    ///     Resolves branding for the identity UI. The seam maps <c>client_id</c> to a
    ///     distribution's branding today (a tenant hint later); because branding is a token set,
    ///     adding per-tenant branding later is data, not code. Defaults to Tellma branding.
    /// </summary>
    public interface IBrandingResolver
    {
        /// <summary>Resolves the branding for a request.</summary>
        /// <param name="clientId">The requesting client, when the page is part of a flow.</param>
        /// <returns>The branding tokens.</returns>
        BrandingInfo Resolve(string? clientId);
    }

    /// <summary>The default (and currently only) branding: Tellma.</summary>
    public sealed class DefaultBrandingResolver : IBrandingResolver
    {
        /// <inheritdoc />
        public BrandingInfo Resolve(string? clientId)
        {
            return new BrandingInfo(
                "Tellma",
                LogoPath: null,
                WordmarkPath: "~/_content/Tellma.Identity/img/tellma-wordmark.svg",
                WordmarkOnDarkPath: "~/_content/Tellma.Identity/img/tellma-wordmark-on-dark.svg");
        }
    }
}
