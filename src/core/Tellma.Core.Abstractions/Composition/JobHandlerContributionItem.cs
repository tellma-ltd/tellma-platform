// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A background job handler a feature registers. The platform reads the class's
    ///     <see cref="Jobs.JobHandlerAttribute" />, infers its shape from the handler interface it
    ///     implements, and adds it to the handler registry.
    /// </summary>
    /// <param name="HandlerType">The handler class.</param>
    public sealed record JobHandlerContributionItem(Type HandlerType) : FeatureContributionItem;
}
