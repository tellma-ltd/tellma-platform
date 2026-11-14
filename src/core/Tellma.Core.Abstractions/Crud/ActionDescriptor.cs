// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     What every action of a stack or route shares, whichever attribute declared it. The kinds are
    ///     closed: an <see cref="EntityActionDescriptor" /> for an entity action, reached through the
    ///     service's action operation, and an <see cref="ApiActionDescriptor" /> for an API action,
    ///     reached through the action invoker.
    /// </summary>
    public abstract record ActionDescriptor
    {
        /// <summary>Initialises the members both kinds share.</summary>
        /// <param name="name">The action's whole route under the stack or route segment: one or more kebab-case segments joined by <c>/</c>.</param>
        /// <param name="description">The action's tool text; null when it has none.</param>
        /// <param name="mutation">Whether the action writes tenant data.</param>
        /// <param name="allowWhenReadOnly">Whether a read-only tenant still admits the mutation.</param>
        /// <param name="idempotent">Whether a retry is safe.</param>
        /// <param name="long">Whether the action runs under the long request timeout.</param>
        /// <param name="destructive">Whether the action asks for confirmation before it runs.</param>
        /// <param name="mcp">How the action is exposed as an agent tool.</param>
        /// <param name="contractRevision">Bumped by hand when the action's meaning changes with no change of shape.</param>
        /// <param name="resultType">What the caller receives.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="resultType" /> is null.</exception>
        private protected ActionDescriptor(
            string name,
            string? description,
            bool mutation,
            bool allowWhenReadOnly,
            bool idempotent,
            bool @long,
            bool destructive,
            McpExposure mcp,
            int contractRevision,
            Type resultType)
        {
            // The constructor is private protected so the two kinds stay the only ones: a switch
            // over a descriptor's kind is exhaustive.
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(resultType);
            Name = name;
            Description = description;
            Mutation = mutation;
            AllowWhenReadOnly = allowWhenReadOnly;
            Idempotent = idempotent;
            Long = @long;
            Destructive = destructive;
            Mcp = mcp;
            ContractRevision = contractRevision;
            ResultType = resultType;
        }

        /// <summary>The action's whole route under the stack or route segment (<c>invite</c>, <c>preferences/set</c>), unique per stack; also its agent action name.</summary>
        public string Name { get; }

        /// <summary>The action's tool text; null when it has none.</summary>
        public string? Description { get; }

        /// <summary>
        ///     Whether the action writes tenant data. The tenant-state verdict and the agent read-only
        ///     gate read this and nothing else; a read-shaped action declares false.
        /// </summary>
        public bool Mutation { get; }

        /// <summary>Whether a read-only tenant still admits this mutation; a frozen tenant refuses every mutation regardless. Meaningless without <see cref="Mutation" />.</summary>
        public bool AllowWhenReadOnly { get; }

        /// <summary>Whether a retry is safe. Says nothing about writing: a mutation may be retry-safe.</summary>
        public bool Idempotent { get; }

        /// <summary>Whether the action is long-running, and so runs under the long request timeout.</summary>
        [SuppressMessage(
            "Naming",
            "CA1720:Identifier contains type name",
            Justification = "The contract's established name, shared with the action attributes and the client's contracts metadata.")]
        public bool Long { get; }

        /// <summary>Whether the action asks the caller to confirm before it runs.</summary>
        public bool Destructive { get; }

        /// <summary>How the action is exposed as an agent tool.</summary>
        public McpExposure Mcp { get; }

        /// <summary>Bumped by hand when the action's meaning changes with no change of shape; enters its endpoint's contract fingerprint.</summary>
        public int ContractRevision { get; }

        /// <summary>
        ///     What the caller receives: for an entity action, the entities result of the stack's entity
        ///     when the method returns no value, else the method's own result type; for an API action,
        ///     the method's result type.
        /// </summary>
        public Type ResultType { get; }
    }

    /// <summary>
    ///     A built-in action (<c>activate</c>, <c>deactivate</c>) or a method marked as an entity
    ///     action on the stack's service: a bulk command over target rows by id, under the stack's
    ///     securable for <see cref="Action" />.
    /// </summary>
    /// <param name="Name">The action's whole route under the stack segment.</param>
    /// <param name="Description">The action's tool text; null when it has none.</param>
    /// <param name="Mutation">Whether the action writes tenant data.</param>
    /// <param name="AllowWhenReadOnly">Whether a read-only tenant still admits the mutation.</param>
    /// <param name="Idempotent">Whether a retry is safe.</param>
    /// <param name="Long">Whether the action runs under the long request timeout.</param>
    /// <param name="Destructive">Whether the action asks for confirmation before it runs.</param>
    /// <param name="Mcp">How the action is exposed as an agent tool.</param>
    /// <param name="ContractRevision">Bumped by hand when the action's meaning changes with no change of shape.</param>
    /// <param name="ResultType">What the caller receives.</param>
    /// <param name="Action">The securable action the action requires on the stack's resource (<c>Activate</c>, <c>Invite</c>).</param>
    /// <param name="SupportsFilter">Whether the grant's filter applies to the target rows; false makes the grant all-or-nothing.</param>
    /// <param name="LoadChildren">Whether the target rows arrive with every child collection populated.</param>
    /// <param name="PostCheck">Whether the grant's filter is re-run over the target rows after the write.</param>
    /// <param name="SingleTarget">Whether the action targets exactly one row and answers its own result.</param>
    /// <param name="IsBuiltIn">Whether a capability declares the action rather than a service method.</param>
    /// <param name="ArgumentsType">The method's arguments record; null when it takes none.</param>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Long is the contract's established name, shared with the action attributes and the client's contracts metadata.")]
    public sealed record EntityActionDescriptor(
        string Name,
        string? Description,
        bool Mutation,
        bool AllowWhenReadOnly,
        bool Idempotent,
        bool Long,
        bool Destructive,
        McpExposure Mcp,
        int ContractRevision,
        Type ResultType,
        string Action,
        bool SupportsFilter,
        bool LoadChildren,
        bool PostCheck,
        bool SingleTarget,
        bool IsBuiltIn,
        Type? ArgumentsType)
        : ActionDescriptor(Name, Description, Mutation, AllowWhenReadOnly, Idempotent, Long, Destructive, Mcp, ContractRevision, ResultType);

    /// <summary>
    ///     A method marked as an API action on an entity service, a stack companion or a routed
    ///     service: a single-body operation the action invoker reaches after evaluating
    ///     <see cref="Securable" />.
    /// </summary>
    /// <param name="Name">The action's whole route under the stack or route segment.</param>
    /// <param name="Description">The action's tool text; null when it has none.</param>
    /// <param name="Mutation">Whether the action writes tenant data.</param>
    /// <param name="AllowWhenReadOnly">Whether a read-only tenant still admits the mutation.</param>
    /// <param name="Idempotent">Whether a retry is safe.</param>
    /// <param name="Long">Whether the action runs under the long request timeout.</param>
    /// <param name="Destructive">Whether the action asks for confirmation before it runs.</param>
    /// <param name="Mcp">How the action is exposed as an agent tool.</param>
    /// <param name="ContractRevision">Bumped by hand when the action's meaning changes with no change of shape.</param>
    /// <param name="ResultType">The method's result type.</param>
    /// <param name="Securable">
    ///     The securable the invoker evaluates: the stack's resource for an action of the entity
    ///     service or a stack companion, the attribute's resource on a routed service. Null exactly
    ///     for a member-only action, which any connected active member may call.
    /// </param>
    /// <param name="RequestType">The body's type; null when the method takes none.</param>
    /// <param name="HandlerType">The type declaring the method, which the invoker resolves per request: the service, the companion closed over the leaf, or the routed service.</param>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Long is the contract's established name, shared with the action attributes and the client's contracts metadata.")]
    public sealed record ApiActionDescriptor(
        string Name,
        string? Description,
        bool Mutation,
        bool AllowWhenReadOnly,
        bool Idempotent,
        bool Long,
        bool Destructive,
        McpExposure Mcp,
        int ContractRevision,
        Type ResultType,
        SecurableRef? Securable,
        Type? RequestType,
        Type HandlerType)
        : ActionDescriptor(Name, Description, Mutation, AllowWhenReadOnly, Idempotent, Long, Destructive, Mcp, ContractRevision, ResultType);
}
