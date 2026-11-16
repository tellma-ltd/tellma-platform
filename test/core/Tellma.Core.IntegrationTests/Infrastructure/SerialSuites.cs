// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Suites that hold a server-wide lock, such as the <c>model</c> database every creation
    ///     copies, run alone, after the parallel suites: every other suite creates databases on the
    ///     same server, and would stall behind the lock or disturb what the suite measures.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SerialSuites
    {
        /// <summary>The collection's name.</summary>
        public const string Name = "Serial SQL Server";
    }
}
