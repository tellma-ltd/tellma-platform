// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>
    ///     How the distribution's <see cref="IIdentityServerClient" /> reaches its identity server, bound
    ///     from <c>Tellma:Identity</c> (the section the web host's sign-in options bind from too) and
    ///     validated at startup by the web host and the migrator alike.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Authority" /> is absolute, <c>https</c> outside Development, and carries no
    ///         trailing slash, query, fragment or user information. In in-proc identity mode
    ///         (<c>Tellma:Identity:Mode = InProc</c>) the platform sets it to the in-proc engine's issuer,
    ///         <c>{Tellma:PublicOrigin}/id</c>, whatever the section says.
    ///     </para>
    ///     <para>
    ///         <see cref="ClientId" /> is not a configuration key: the platform sets it after binding to
    ///         the distribution's service client, <c>&lt;slug&gt;-svc</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="ServiceClientSecret" /> is required, except in Development in in-proc mode, where
    ///         the web host generates one for the boot when none is configured, and a host without the
    ///         in-proc engine (the migrator) has none to call it with. It is never quoted by a validation
    ///         message or a log.
    ///     </para>
    /// </remarks>
    public sealed class IdentityServerClientOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Identity";

        /// <summary>The path the in-proc identity engine is served under, below the public origin.</summary>
        public const string InProcPath = "/id";

        /// <summary>What the distribution's slug is suffixed with to name its service client.</summary>
        public const string ServiceClientSuffix = "-svc";

        /// <summary>The identity server's issuer URL, its path included, such as <c>https://identity.tellma.com</c>.</summary>
        public Uri? Authority { get; set; }

        /// <summary>The distribution's service client id, <c>&lt;slug&gt;-svc</c>; set by the platform after binding.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>The secret the service client authenticates with at the identity server's token endpoint.</summary>
        public string? ServiceClientSecret { get; set; }

        /// <summary>
        ///     The audience a service account's tokens are issued for: the distribution's public origin,
        ///     <c>Tellma:PublicOrigin</c>. Not a configuration key; the platform sets it after binding.
        /// </summary>
        internal string? ServiceAccountResource { get; set; }
    }
}
