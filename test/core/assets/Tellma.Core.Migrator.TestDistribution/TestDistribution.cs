// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;

namespace Tellma.Core.Migrator.TestDistribution
{
    /// <summary>
    ///     The test distribution: the core feature and the probe feature, composed exactly as a
    ///     distribution's <c>Compose</c> method composes its features.
    /// </summary>
    public static class TestDistribution
    {
        /// <summary>
        ///     The distribution slug: the reference distribution's, the one slug the repository's
        ///     taxonomy lists, which the Development startup gate checks.
        /// </summary>
        public const string Slug = "acme";

        /// <summary>The distribution's composition method, shared by its migrator and its design-time factory.</summary>
        /// <param name="builder">The composition.</param>
        public static void Compose(TellmaBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            _ = builder.AddFeature(new ProbeFeature());
        }
    }
}
