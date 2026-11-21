// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>
    ///     Suites that hold the process-wide PDF render gate or count Skia's live native objects run
    ///     alone, after the parallel suites: either would disturb, or be disturbed by, another
    ///     suite's renders.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SerialSuites
    {
        /// <summary>The collection's name.</summary>
        public const string Name = "Serial imaging";
    }
}
