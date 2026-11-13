// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Every securable the deployment registers: the role editor's data source, the validators'
    ///     oracle and the drift oracle. A singleton, immutable once composed.
    /// </summary>
    /// <remarks>
    ///     Resources and actions compare ordinal-ignore-case and keep the spelling they were registered
    ///     with. An alias resolves on <see cref="Find" /> alone: <see cref="All" />,
    ///     <see cref="Resources" /> and <see cref="ForResource" /> list registered names only.
    /// </remarks>
    public interface ISecurableRegistry
    {
        /// <summary>Every registered securable, ordered by resource, then action, ignoring case.</summary>
        IReadOnlyList<SecurableDescriptor> All { get; }

        /// <summary>Every registered resource once, in the order of <see cref="All" />.</summary>
        IReadOnlyList<string> Resources { get; }

        /// <summary>
        ///     The lowercase hex SHA-256 of the registry's content. It changes only when a deployment
        ///     changes the securables, and is never stored in a tenant database.
        /// </summary>
        string Fingerprint { get; }

        /// <summary>
        ///     Finds a securable by its pair, ignoring case, resolving a renamed resource through its
        ///     alias.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>The securable, or null for an unknown pair.</returns>
        SecurableDescriptor? Find(string resource, string action);

        /// <summary>The securables of one registered resource, ignoring case, in action order; empty for any other name.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The resource's securables.</returns>
        IReadOnlyList<SecurableDescriptor> ForResource(string resource);
    }
}
