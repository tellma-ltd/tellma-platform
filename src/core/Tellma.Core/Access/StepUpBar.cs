// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Access
{
    // The bar is spec 0011's step-up bar, which spec 0014 §4.3 applies to the sensitive securables.
    /// <summary>
    ///     The assurance bar a sensitive securable asks of the caller's session. A singleton: the
    ///     defaults below, which the web host replaces with its configured session bar.
    /// </summary>
    /// <param name="Requirement">The bar: the authentication context class, and how recently it must have been reached.</param>
    internal sealed record StepUpBar(RequireAssuranceMetadata Requirement)
    {
        /// <summary>The authentication context class required by default.</summary>
        public const string DefaultAcr = "urn:tellma:acr:mfa";

        /// <summary>How recently the class must have been reached by default.</summary>
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(15);

        /// <summary>The default bar, of a composition without a web host.</summary>
        public static StepUpBar Default { get; } = new(new RequireAssuranceMetadata(DefaultAcr, DefaultMaxAge));

        /// <summary>The bar.</summary>
        public RequireAssuranceMetadata Requirement { get; } = Requirement ?? throw new ArgumentNullException(nameof(Requirement));
    }
}
