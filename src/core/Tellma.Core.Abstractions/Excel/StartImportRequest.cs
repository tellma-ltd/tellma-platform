// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>The body of <c>import/start</c>: an import that runs in the background.</summary>
    public sealed class StartImportRequest : ImportRequest
    {
        /// <summary>
        ///     Whether the whole file commits in one transaction rather than in chunks; false by
        ///     default. A file above the atomic limits fails the job before anything is written.
        /// </summary>
        public bool Atomic { get; set; }
    }
}
