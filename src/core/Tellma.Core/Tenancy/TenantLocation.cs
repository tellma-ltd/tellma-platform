// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Where a tenant's database lives: a server, a database and the name of the credential
    ///     profile its connections use. Never a connection string and never a credential.
    /// </summary>
    /// <param name="Server">The server: <c>[tcp:]host[\instance][,port]</c>, whose host must be allowed by <c>Tellma:Sql:AllowedServers</c>.</param>
    /// <param name="Database">The database name.</param>
    /// <param name="CredentialProfile">The name of a profile under <c>Tellma:Sql:Profiles</c>.</param>
    public sealed record TenantLocation(string Server, string Database, string CredentialProfile);
}
