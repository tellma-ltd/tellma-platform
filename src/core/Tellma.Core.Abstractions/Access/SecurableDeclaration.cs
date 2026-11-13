// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>One <see cref="SecurableRegistryBuilder.Add" /> call, as made.</summary>
    /// <param name="Resource">The resource.</param>
    /// <param name="Action">The action.</param>
    /// <param name="FilterRoot">The filter root, or null.</param>
    /// <param name="IsSensitive">Whether the call marked it sensitive.</param>
    /// <param name="Owner">The owner, or null.</param>
    /// <param name="Feature">The feature the call named, or null for the contributor's.</param>
    /// <param name="Contributor">The name of the feature whose contributor made the call.</param>
    internal sealed record SecurableDeclaration(
        string Resource,
        string Action,
        string? FilterRoot,
        bool IsSensitive,
        SecurableOwner? Owner,
        string? Feature,
        string Contributor);
}
