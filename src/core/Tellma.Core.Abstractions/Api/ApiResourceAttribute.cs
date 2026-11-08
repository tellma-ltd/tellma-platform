// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Adjusts how an entity's stack, or a child entity's query surface, is projected onto the web
    ///     surface and the agent tools. Optional: a registered stack is projected without it.
    ///     Inherited, so a distribution's leaf keeps its pack default's declaration unless it
    ///     declares its own.
    /// </summary>
    /// <example>
    ///     <code>[ApiResource(Segment = "support-requests", Mcp = McpExposure.Hidden)]</code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ApiResourceAttribute : Attribute
    {
        /// <summary>
        ///     The route segment, overriding the kebab-case plural of the table name
        ///     (<c>gl.InvoiceLines</c> → <c>invoice-lines</c>); null keeps the default. Routing only:
        ///     never a permission key.
        /// </summary>
        public string? Segment { get; set; }

        /// <summary>The stack's description for agents and generated documentation; null for the entity's XML summary.</summary>
        public string? Description { get; set; }

        /// <summary>How the agent tools see the stack. <see cref="McpExposure.Full" /> by default.</summary>
        public McpExposure Mcp { get; set; } = McpExposure.Full;

        /// <summary>
        ///     The stack's contract revision, bumped by hand when its wire meaning changes with no
        ///     change of shape; it enters the contract fingerprint of every endpoint of the stack. 0 by
        ///     default.
        /// </summary>
        public int ContractRevision { get; set; }

        /// <summary>
        ///     On a child entity only: whether the child gets a read-only query surface of its own.
        ///     False keeps it off the child query surfaces. True by default.
        /// </summary>
        public bool Queryable { get; set; } = true;
    }
}
