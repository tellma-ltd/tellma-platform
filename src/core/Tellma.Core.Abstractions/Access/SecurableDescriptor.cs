// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A registered securable: the pair a permission targets, the entity its filters bind against,
    ///     whether it asks for a recent sign-in, the feature it belongs to and, for a child, its owner.
    /// </summary>
    /// <param name="Resource">The resource as registered, such as <c>gl.Center</c>; compared ignoring case.</param>
    /// <param name="Action">The action as registered, such as <c>Save</c>; compared ignoring case.</param>
    /// <param name="FilterRoot">
    ///     The name of the query entity a grant's filter binds against, such as <c>gl.Center</c>; null
    ///     when the securable takes no filter. A name rather than a schema descriptor, because each
    ///     tenant's schema is built for its own configuration.
    /// </param>
    /// <param name="IsSensitive">Whether exercising the securable asks for a recent, stronger sign-in (step-up).</param>
    /// <param name="Feature">The name of the feature the securable belongs to, which groups it in the permissions editor.</param>
    /// <param name="Owner">The owner of a child securable; null for any other.</param>
    public sealed record SecurableDescriptor(
        string Resource,
        string Action,
        string? FilterRoot,
        bool IsSensitive,
        string Feature,
        SecurableOwner? Owner)
    {
        /// <summary>Whether a grant of the securable may carry a row-level filter: it has a filter root.</summary>
        public bool SupportsFilter => FilterRoot is not null;

        /// <summary>The display form, <c>resource:action</c>.</summary>
        /// <returns>The pair as text.</returns>
        public override string ToString()
        {
            return $"{Resource}:{Action}";
        }
    }
}
