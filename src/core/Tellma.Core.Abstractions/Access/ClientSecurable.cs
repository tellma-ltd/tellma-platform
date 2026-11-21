// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One registered securable as the client metadata lists it, with the string keys of its
    ///     labels resolved when the metadata is emitted, so the client never walks a key chain.
    /// </summary>
    /// <remarks>
    ///     Each label key is the first key of its chain that a neutral string resource declares, and
    ///     its fallback is then null. When no resource declares a key of the chain, the key is the
    ///     chain's conventional key and the fallback is humanized English for the client to show.
    /// </remarks>
    /// <param name="Resource">The resource as registered.</param>
    /// <param name="Action">The action as registered.</param>
    /// <param name="FilterRoot">The query entity a grant's filter binds against; null when the securable takes no filter.</param>
    /// <param name="IsSensitive">Whether exercising the securable asks for a recent sign-in.</param>
    /// <param name="Feature">The name of the feature the securable belongs to.</param>
    /// <param name="Owner">The owner of a child securable; null for any other.</param>
    /// <param name="ResourceLabelKey">The string key of the resource's label.</param>
    /// <param name="ResourceLabelFallback">The resource's label when no resource declares a key of its chain; otherwise null.</param>
    /// <param name="ActionLabelKey">The string key of the action's label.</param>
    /// <param name="ActionLabelFallback">The action's label when no resource declares a key of its chain; otherwise null.</param>
    /// <param name="FeatureLabelKey">The string key of the feature's label, which groups securables in the permissions editor.</param>
    /// <param name="FeatureLabelFallback">The feature's label when no resource declares its key; otherwise null.</param>
    public sealed record ClientSecurable(
        string Resource,
        string Action,
        string? FilterRoot,
        bool IsSensitive,
        string Feature,
        SecurableOwner? Owner,
        string ResourceLabelKey,
        string? ResourceLabelFallback,
        string ActionLabelKey,
        string? ActionLabelFallback,
        string FeatureLabelKey,
        string? FeatureLabelFallback);
}
