// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>One <see cref="SecurableRegistryBuilder.MarkSensitive" /> or <see cref="SecurableRegistryBuilder.MarkNotSensitive" /> call, as made.</summary>
    /// <param name="Resource">The resource.</param>
    /// <param name="Action">The action.</param>
    /// <param name="Sensitive">True for <see cref="SecurableRegistryBuilder.MarkSensitive" />, false for <see cref="SecurableRegistryBuilder.MarkNotSensitive" />.</param>
    /// <param name="Contributor">The name of the feature whose contributor made the call.</param>
    internal sealed record SecurableMarkDeclaration(string Resource, string Action, bool Sensitive, string Contributor);
}
