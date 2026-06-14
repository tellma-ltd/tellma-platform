// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data;
using Microsoft.Data.SqlClient.Server;

namespace Tellma.Core.EntityFrameworkCore.Tests.Internal
{
    /// <summary>
    ///     Pins the load-bearing driver premise for the JSON-column wire form (spec 0001 §2): the
    ///     resolved Microsoft.Data.SqlClient (6.1.1) cannot construct a <c>SqlMetaData</c> for SQL
    ///     Server 2025's native <c>json</c> type, so a json column is not bindable as a table-valued
    ///     parameter column — which is why JSON columns are carried as <c>varchar(max)</c> /
    ///     <c>nvarchar(max)</c>. If a future driver makes json bindable, this fails loudly, prompting a
    ///     re-read of the JSON-columns decision (which still holds on the transient-parameter rationale).
    /// </summary>
    public class JsonDriverCapabilityTests
    {
        [Fact]
        public void SqlMetaData_cannot_represent_the_native_json_type()
        {
            Assert.ThrowsAny<Exception>(() => new SqlMetaData("payload", SqlDbTypeExtensions.Json));
        }
    }
}
