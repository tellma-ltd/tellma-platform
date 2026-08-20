// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Development affordances. Every flag defaults to off so a production configuration that
    ///     forgets this section stays secure.
    /// </summary>
    public sealed class TellmaIdentityDevelopmentOptions
    {
        /// <summary>
        ///     Permit the <see cref="TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned" />
        ///     key source. Startup fails when that source is selected without this flag.
        /// </summary>
        public bool AllowDevelopmentCertificates { get; set; }

        /// <summary>
        ///     Permit protocol endpoints over plain HTTP (local development and in-memory test
        ///     hosts only).
        /// </summary>
        public bool AllowInsecureHttp { get; set; }
    }
}
