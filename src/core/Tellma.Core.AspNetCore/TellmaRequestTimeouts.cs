// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.AspNetCore
{
    /// <summary>The names of the platform's request-timeout policies.</summary>
    public static class TellmaRequestTimeouts
    {
        /// <summary>The ordinary policy of the web, blob and hub endpoints.</summary>
        public const string Web = "tellma-web";

        /// <summary>
        ///     The policy of an operation that runs long, such as an export, an import or a report.
        ///     Every projected route whose action runs long carries it, and a hand-mapped endpoint
        ///     that runs long takes it itself.
        /// </summary>
        [SuppressMessage(
            "Naming",
            "CA1720:Identifier contains type name",
            Justification = "Long is the contract's established name, shared with the action descriptors and the client's contracts metadata.")]
        public const string Long = "tellma-long";

        /// <summary>The policy of the agent tool endpoint, one tool call per request.</summary>
        public const string Mcp = "tellma-mcp";
    }
}
