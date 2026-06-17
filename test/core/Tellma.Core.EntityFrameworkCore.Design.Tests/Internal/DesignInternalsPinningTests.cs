// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.Internal
{
    /// <summary>
    ///     Pins the internal EF Core Design surface the design-time companion derives from — the
    ///     design-time analogue of the runtime library's <c>EfInternalsPinningTests</c>. The Design
    ///     project absorbs design-time coupling by policy (it never ships in a runtime host); the
    ///     surface is already compiler-enforced, and this assertion makes an EF upgrade that relocates
    ///     the base type fail with a clear pointer to the single file that must change.
    /// </summary>
    public class DesignInternalsPinningTests
    {
        [Fact]
        public void Annotation_code_generator_base_is_the_sql_server_internal_type()
        {
            Assert.Equal(
                "Microsoft.EntityFrameworkCore.SqlServer.Design.Internal.SqlServerAnnotationCodeGenerator",
                typeof(TableTypesSqlServerAnnotationCodeGenerator).BaseType!.FullName);
        }
    }
}
