// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    // The root of a wildcard-action filter is spec 0014 §3.7 rule 2.
    /// <summary>
    ///     The filter root a stored permission's filter binds against: its securable's, or, for every
    ///     action of a resource, the one root the resource's filterable securables share.
    /// </summary>
    internal static class SecurableFilterRoots
    {
        /// <summary>The filter root a permission on a resolved pair binds against.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="resource">The resource as registered.</param>
        /// <param name="action">The action, or the wildcard.</param>
        /// <returns>
        ///     The filter root; null when the securable takes no filter, when the pair is not
        ///     registered, or, for the wildcard, when the resource's securables share no one root.
        /// </returns>
        public static string? Of(ISecurableRegistry registry, string resource, string action)
        {
            ArgumentNullException.ThrowIfNull(registry);

            return action == AccessActions.Wildcard
                ? Shared(registry.ForResource(resource))
                : registry.Find(resource, action)?.FilterRoot;
        }

        /// <summary>
        ///     The one filter root a resource's filterable securables share. The startup gate refuses a
        ///     resource whose securables name two, so null means none takes a filter.
        /// </summary>
        /// <param name="securables">The resource's securables.</param>
        /// <returns>The shared root; null when none takes a filter, or when they name more than one.</returns>
        public static string? Shared(IReadOnlyList<SecurableDescriptor> securables)
        {
            ArgumentNullException.ThrowIfNull(securables);

            // Two roots would leave a wildcard-action filter bound against one of them and applied
            // to the actions of the other; refusing to choose fails closed.
            string? shared = null;
            foreach (SecurableDescriptor securable in securables)
            {
                if (securable.FilterRoot is not string root)
                {
                    continue;
                }

                if (shared is null)
                {
                    shared = root;
                }
                else if (!string.Equals(shared, root, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return shared;
        }
    }
}
