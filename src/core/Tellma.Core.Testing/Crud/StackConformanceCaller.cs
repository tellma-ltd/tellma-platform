// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Testing.Crud
{
    /// <summary>A user a conformance harness seeded, whose scopes the suite runs its operations in.</summary>
    /// <param name="UserId">The user's id in the tenant database.</param>
    /// <param name="Subject">The user's subject, which the connect prologue resolves the user by.</param>
    public sealed record StackConformanceCaller(int UserId, string Subject);
}
