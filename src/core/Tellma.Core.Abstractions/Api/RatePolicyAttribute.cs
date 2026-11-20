// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Puts an action's web endpoint, or a hand-mapped endpoint, under one of the
    ///     <see cref="TellmaRatePolicies" />, chained ahead of the web surface's own
    ///     <see cref="TellmaRatePolicies.User" /> and <see cref="TellmaRatePolicies.Tenant" />. The
    ///     startup audit refuses an unknown name, two route policies on one endpoint and a priority on
    ///     any policy but <see cref="TellmaRatePolicies.Reports" />.
    /// </summary>
    /// <example>
    ///     <code>
    ///     [ApiAction("export", Mutation = false, Long = true)]
    ///     [RatePolicy(TellmaRatePolicies.Export)]
    ///     public Task&lt;ExportOutcome&gt; ExportAsync(ExportRequest request);
    ///     </code>
    /// </example>
    /// <param name="policy">The policy's name, one of <see cref="TellmaRatePolicies" />.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RatePolicyAttribute(string policy) : Attribute, IRatePolicyMetadata
    {
        /// <inheritdoc />
        public string Policy { get; } = policy;

        /// <inheritdoc />
        public bool HonorsPriority { get; init; }
    }
}
