// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares a single-body operation that is not id-shaped: a public instance method on an
    ///     entity service, on a stack companion or on a <see cref="ApiRouteAttribute" /> service,
    ///     taking at most one body parameter (and optionally a cancellation token) and returning a
    ///     task of a result or a bare task. The attribute is the security boundary: no public method
    ///     becomes an operation by convention.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every such method is reached through the platform's one action invoker, which evaluates
    ///         the securable before the method runs; the platform's analyzers refuse a direct call
    ///         from outside the declaring type.
    ///     </para>
    ///     <para>
    ///         <see cref="Name" /> follows the rules of <see cref="EntityActionAttribute.Name" />
    ///         (<c>me/save</c>, <c>invitation-status</c>). The securable is the stack's resource with
    ///         <see cref="Action" /> on an entity service or a companion, and
    ///         <see cref="Resource" /> with <see cref="Action" /> on a route service, which must name
    ///         <see cref="Resource" /> unless <see cref="MemberOnly" />.
    ///     </para>
    ///     <para>
    ///         Inherited, so an override of an action method stays the action it overrides.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>[ApiAction("invitation-status", Action = "Read", Idempotent = true, Mutation = false)]</code>
    /// </example>
    /// <param name="name">The operation's name: its route under the stack or route segment and its agent action name.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ApiActionAttribute(string name) : Attribute
    {
        /// <summary>The operation's name: its route under the stack or route segment and its agent action name.</summary>
        public string Name { get; } = name;

        /// <summary>
        ///     The securable action the operation requires; null for the PascalCase form of the name's
        ///     last segment. Ignored when <see cref="MemberOnly" />.
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        ///     The securable resource of an operation on a route service, such as
        ///     <c>acme.CreditExposureReport</c>; required there unless <see cref="MemberOnly" />. On an
        ///     entity service or a companion the stack's resource is used and this is null.
        /// </summary>
        public string? Resource { get; set; }

        /// <summary>
        ///     Whether any connected active member may call the operation, with no securable. False by
        ///     default.
        /// </summary>
        public bool MemberOnly { get; set; }

        /// <summary>
        ///     Whether the operation writes tenant data, which the tenant-state verdicts and the agent
        ///     read-only gate read. True by default; a read-shaped operation declares false.
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

        /// <summary>Whether the operation runs long, under the longer request timeout. False by default.</summary>
        [SuppressMessage(
            "Naming",
            "CA1720:Identifier contains type name",
            Justification = "The contract's established name, shared by every action descriptor and the client's contracts metadata.")]
        public bool Long { get; set; }

        /// <summary>Whether an agent must confirm the operation before it runs. False by default.</summary>
        public bool Destructive { get; set; }

        /// <summary>How agents see the operation; <see cref="McpExposure.Hidden" /> keeps it off the agent tools. <see cref="McpExposure.Full" /> by default.</summary>
        public McpExposure Mcp { get; set; } = McpExposure.Full;

        /// <summary>The operation's description for agents and generated documentation; null for none.</summary>
        public string? Description { get; set; }

        /// <summary>
        ///     The operation's contract revision, bumped by hand when its meaning changes with no change
        ///     of shape; it enters the operation's contract fingerprint. 0 by default.
        /// </summary>
        public int ContractRevision { get; set; }
    }
}
