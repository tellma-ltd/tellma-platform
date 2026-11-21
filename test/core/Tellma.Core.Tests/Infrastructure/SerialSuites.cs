// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>
    ///     Suites that measure the process's managed heap run alone, after the parallel suites: every
    ///     other suite's allocations land on the same heap and would read as the measured code's.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SerialSuites
    {
        /// <summary>The collection's name.</summary>
        public const string Name = "Serial core";
    }
}
