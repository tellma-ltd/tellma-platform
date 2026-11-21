// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>The general ledger module's identity, as a feature that builds on it names it.</summary>
    public static class GlModule
    {
        /// <summary>
        ///     The GL feature's name: what a dependent feature requires to build on GL, such as one that
        ///     replaces its sample centers or extends its center entity.
        /// </summary>
        public const string FeatureName = "gl";

        /// <summary>The database schema of the module's tables.</summary>
        public const string Schema = "gl";
    }
}
