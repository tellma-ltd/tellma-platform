// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The collection the server tests share: one SQL Server for the assembly, a container of its
    ///     own unless an existing server is named. Its tests run one at a time, so a test that holds
    ///     the <c>model</c> lock never overlaps another test's creations or drops.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerFixture>
    {
        /// <summary>The collection's name.</summary>
        public const string Name = "SQL Server";
    }
}
