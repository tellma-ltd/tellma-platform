// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>A tenant to register in the catalog.</summary>
    /// <param name="Name">The display name, until the tenant's settings are first saved; 1 to 255 characters.</param>
    /// <param name="Category">Live or sandbox; fixed from now on.</param>
    /// <param name="LiveTenantId">The live tenant a sandbox derives from; null for a live tenant and a standalone sandbox.</param>
    /// <param name="Location">
    ///     Where the tenant's database lives; null for the catalog's own server, a database named
    ///     <c>&lt;DatabasePrefix&gt;.&lt;Id&gt;</c> and the <c>default</c> profile.
    /// </param>
    public sealed record TenantRegistration(string Name, TenantCategory Category, int? LiveTenantId, TenantLocation? Location);
}
