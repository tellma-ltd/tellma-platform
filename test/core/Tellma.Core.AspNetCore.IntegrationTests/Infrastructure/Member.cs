// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>Someone the suite signs in: a tenant's member, a former member, or a stranger to every tenant.</summary>
    /// <param name="Subject">The identity server's subject, which a session carries.</param>
    /// <param name="Email">The email address.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="UserId">The user's id in the live tenant; null for someone no tenant knows.</param>
    internal sealed record Member(string Subject, string Email, string Name, int? UserId);
}
