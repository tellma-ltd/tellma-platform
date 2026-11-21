// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A tenant provisioning step a feature contributes: a concrete class implementing
    ///     <see cref="Tenancy.ITenantProvisioningStep" />. The platform registers it as a scoped
    ///     service, so the step is created inside the tenant scope it runs in.
    /// </summary>
    /// <param name="StepType">The step class.</param>
    public sealed record ProvisioningStepContributionItem(Type StepType) : FeatureContributionItem;
}
