// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Analyzers.Tests.Infrastructure
{
    /// <summary>
    ///     The one platform type the rules match that a test compilation does not reference: the
    ///     runtime's tenant context. The test compilations reference the contract, not the runtime,
    ///     so that a test may take the runtime's assembly name. The rules match the context by
    ///     namespace and metadata name, which this declaration shares with the real one; the name
    ///     pins hold the real type to the same name.
    /// </summary>
    internal static class PlatformStubs
    {
        /// <summary>The stub's source, compiled into every test compilation beside the test's own source.</summary>
        public const string Source = """
            using Microsoft.EntityFrameworkCore;
            using Tellma.Core.Abstractions.Jobs;

            namespace Tellma.Core.Data
            {
                public sealed class TellmaDbContext : DbContext
                {
                    public DbSet<Job> Jobs { get; set; } = null!;
                }
            }
            """;
    }
}
