// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>One <see cref="SecurableRegistryBuilder.Alias" /> call, as made.</summary>
    /// <param name="OldResource">The resource's former name.</param>
    /// <param name="NewResource">The registered resource it now is.</param>
    /// <param name="Contributor">The name of the feature whose contributor made the call.</param>
    internal sealed record SecurableAliasDeclaration(string OldResource, string NewResource, string Contributor);
}
