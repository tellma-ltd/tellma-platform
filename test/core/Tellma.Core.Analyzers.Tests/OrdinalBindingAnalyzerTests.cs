// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0001: table-valued parameter records are built and bound by ordinal only inside a binder type.</summary>
    public class OrdinalBindingAnalyzerTests
    {
        /// <summary>Constructing a record outside a binder is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Constructing_a_record_outside_a_binder_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<OrdinalBindingAnalyzer>("""
                using Microsoft.Data.SqlClient.Server;

                class Rows
                {
                    object Make(SqlMetaData[] columns) => {|TELLMA0001:new SqlDataRecord(columns)|};
                }
                """);
        }

        /// <summary>Setting a column by ordinal outside a binder is refused, called or taken as a delegate.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Setting_by_ordinal_outside_a_binder_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<OrdinalBindingAnalyzer>("""
                using System;
                using Microsoft.Data.SqlClient.Server;

                class Rows
                {
                    void Fill(SqlDataRecord record)
                    {
                        {|TELLMA0001:record.SetInt32(0, 42)|};
                        {|TELLMA0001:record.SetDBNull(1)|};
                        int ordinal = {|TELLMA0001:record.GetOrdinal("Id")|};
                        Action<int, string> set = {|TELLMA0001:record.SetString|};
                    }
                }
                """);
        }

        /// <summary>A binder type, and a type nested in one, builds and binds records freely.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_binder_type_builds_and_binds_records()
        {
            return AnalyzerHarness.VerifyAsync<OrdinalBindingAnalyzer>("""
                using Microsoft.Data.SqlClient.Server;
                using Tellma.Core.Abstractions.Entities;

                [TableTypeBinder]
                class Binder
                {
                    SqlDataRecord Make(SqlMetaData[] columns)
                    {
                        SqlDataRecord record = new(columns);
                        record.SetInt32(record.GetOrdinal("Id"), 42);
                        return record;
                    }

                    class Nested
                    {
                        void Fill(SqlDataRecord record) => record.SetDBNull(0);
                    }
                }
                """);
        }

        /// <summary>Reading a record and naming its members are not ordinal binding.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Reading_a_record_is_not_binding()
        {
            return AnalyzerHarness.VerifyAsync<OrdinalBindingAnalyzer>("""
                using Microsoft.Data.SqlClient.Server;

                class Reader
                {
                    object Read(SqlDataRecord record) => record.GetValue(0) ?? record.GetName(0) ?? nameof(SqlDataRecord.SetInt32);
                }
                """);
        }
    }
}
