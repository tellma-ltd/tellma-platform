// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares a bulk action on a stack's entity service: a public instance method taking the
    ///     action context of the target rows, and optionally a typed arguments record, and returning
    ///     <see cref="Task" />, <see cref="ValueTask" />, <see cref="Task{TResult}" /> or
    ///     <see cref="ValueTask{TResult}" />. The attribute is the security boundary: no public method
    ///     becomes an operation by convention.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One declaration projects the action everywhere: its securable, its web route under the
    ///         stack's segment, its agent action and its client metadata. A method returning no value
    ///         answers the target rows read back; a method with a result of its own answers that
    ///         result.
    ///     </para>
    ///     <para>
    ///         <see cref="Name" /> is one or more kebab-case segments joined by <c>/</c>
    ///         (<c>invite</c>, <c>take-over</c>, <c>preferences/set</c>), unique per stack, and never a
    ///         standard segment the stack projects. <see cref="Action" /> defaults to the PascalCase
    ///         form of the name's last segment (<c>Invite</c>, <c>TakeOver</c>, <c>Set</c>), so
    ///         several actions may share one securable. The startup checks report a violation with the
    ///         method named.
    ///     </para>
    ///     <para>
    ///         Inherited, so an override of an action method stays the action it overrides.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>[EntityAction("post", LoadChildren = true, Description = "Posts draft invoices to the ledger.")]</code>
    /// </example>
    /// <param name="name">The action's name: its route under the stack's segment and its agent action name.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class EntityActionAttribute(string name) : Attribute
    {
        /// <summary>The action's name: its route under the stack's segment and its agent action name.</summary>
        public string Name { get; } = name;

        /// <summary>
        ///     The securable action the operation requires on the stack's resource; null for the
        ///     PascalCase form of the name's last segment.
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        ///     Whether the grant's row-level filter applies: the securable is registered with the
        ///     stack's resource as its filter root, and the target rows, loaded under the caller's read
        ///     filter, are counted under the grant's filter. False makes the grant all-or-nothing. True
        ///     by default.
        /// </summary>
        public bool SupportsFilter { get; set; } = true;

        /// <summary>Whether the target rows arrive with every child collection loaded. False by default.</summary>
        public bool LoadChildren { get; set; }

        /// <summary>
        ///     Whether the grant's filter is re-run over the target rows after the write. False by
        ///     default, because an action commonly moves a row out of the filter that permitted it.
        /// </summary>
        public bool PostCheck { get; set; }

        /// <summary>
        ///     Whether the action targets exactly one row: its request carries one id instead of a list,
        ///     and the method must return a result of its own. False by default.
        /// </summary>
        public bool SingleTarget { get; set; }

        /// <summary>
        ///     Whether the action writes tenant data, which the tenant-state verdicts and the agent
        ///     read-only gate read. True by default; a read-shaped action declares false.
        /// </summary>
        public bool Mutation { get; set; } = true;

        /// <summary>
        ///     Whether a read-only tenant still admits this mutation; a frozen tenant refuses every
        ///     mutation regardless. Meaningless without <see cref="Mutation" />. False by default.
        /// </summary>
        public bool AllowWhenReadOnly { get; set; }

        /// <summary>
        ///     Whether a client may retry the call after a lost response. Retry safety only: a mutation
        ///     may be retry-safe. False by default.
        /// </summary>
        public bool Idempotent { get; set; }

        /// <summary>Whether the action runs long, under the longer request timeout. False by default.</summary>
        [SuppressMessage(
            "Naming",
            "CA1720:Identifier contains type name",
            Justification = "The contract's established name, shared by every action descriptor and the client's contracts metadata.")]
        public bool Long { get; set; }

        /// <summary>Whether an agent must confirm the action before it runs. False by default.</summary>
        public bool Destructive { get; set; }

        /// <summary>How agents see the action; <see cref="McpExposure.Hidden" /> keeps it off the agent tools. <see cref="McpExposure.Full" /> by default.</summary>
        public McpExposure Mcp { get; set; } = McpExposure.Full;

        /// <summary>The action's description for agents and generated documentation; null for none.</summary>
        public string? Description { get; set; }

        /// <summary>
        ///     The action's contract revision, bumped by hand when its meaning changes with no change
        ///     of shape; it enters the action's contract fingerprint. 0 by default.
        /// </summary>
        public int ContractRevision { get; set; }
    }
}
