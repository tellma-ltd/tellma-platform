// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Module.Gl
{
    // Spec 0018 §5.5. GL writes no model configuration and registers no securable by hand: the
    // center's annotations produce its table, and the stack's descriptor its securables
    // (gl.Center × Read, Save, Delete, Activate) and its routes under centers/.
    /// <summary>
    ///     The general ledger feature, named <c>gl</c>: the responsibility centers' stack, the
    ///     development tenants' sample centers and their default tree. A distribution adds it to its
    ///     composition; a feature that builds on it requires <see cref="GlModule.FeatureName" />.
    /// </summary>
    /// <remarks>
    ///     A distribution extends the center by inheritance and registers its leaf in place of
    ///     <see cref="Center" />: the service and the provisioning step are closed over the leaf. A
    ///     dependent feature that registers its own <see cref="IGlSampleCenters" /> singleton
    ///     replaces the default tree.
    /// </remarks>
    public sealed class GlFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => GlModule.FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            // GL requires no feature: the platform's own is implicit.
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution
                .Entity<Center, CenterService<Center>>()
                .ProvisioningStep<GlSampleCentersStep<Center>>()
                .Singleton<IGlSampleCenters, DefaultGlSampleCenters>();
        }
    }
}
