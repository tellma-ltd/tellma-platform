// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A feature's transform of a blob kind's policy. The platform applies the features'
    ///     transforms in the features' dependency order, then the host's, each to the result of the
    ///     one before, starting from the policy the kind's <c>[BlobReference]</c> produced. The
    ///     startup gate refuses a transform of a kind no blob reference declares.
    /// </summary>
    /// <param name="Kind">The kind, such as <c>email-attachment</c>.</param>
    /// <param name="Configure">
    ///     The transform: it receives the policy so far and returns the policy to use, usually
    ///     <c>p =&gt; p with { … }</c>; it may return a different policy outright.
    /// </param>
    public sealed record BlobKindContributionItem(string Kind, Func<BlobKindPolicy, BlobKindPolicy> Configure) : FeatureContributionItem;
}
