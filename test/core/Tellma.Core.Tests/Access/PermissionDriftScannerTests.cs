// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The permission drift scanner without a database: the line the migrator's report prints
    ///     for each drifted permission, and the scope it refuses.
    /// </summary>
    public class PermissionDriftScannerTests
    {
        [Fact]
        public void A_drifted_permission_reads_as_its_role_its_problem_and_each_diagnostic_with_its_arguments()
        {
            PermissionDriftItem item = new(
                12,
                4,
                "clerks",
                AccessProblemCode.FilterInvalid,
                [
                    new QueryexDiagnostic("QX3001", new QueryexSpan(0, 4), null, [new("property", "Name2"), new("entity", "fx.Widget")]),
                    new QueryexDiagnostic("QX3003", new QueryexSpan(9, 3), null, []),
                ]);

            Assert.Equal("Permission 12 of role 4 (clerks) grants nothing: FilterInvalid; QX3001 property=Name2 entity=fx.Widget; QX3003.", PermissionDriftScanner.Describe(item));
        }

        [Fact]
        public void A_role_without_a_code_and_a_problem_without_diagnostics_read_as_the_bare_problem()
        {
            PermissionDriftItem item = new(3, 2, null, AccessProblemCode.UnknownResource, []);

            Assert.Equal("Permission 3 of role 2 grants nothing: UnknownResource.", PermissionDriftScanner.Describe(item));
        }

        [Fact]
        public async Task A_user_s_scope_is_refused()
        {
            PermissionDriftScanner scanner = new(new SchemaDatabase(Schema), new PermissionClassifier(Registry, new QueryexEngine()), new FixedContextAccessor(Context(50)));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(TestContext.Current.CancellationToken));
        }
    }
}
