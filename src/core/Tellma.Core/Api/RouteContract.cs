// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Api
{
    /// <summary>
    ///     One projected web endpoint: its route, the operation or action serving it, its body and
    ///     result types, its securable, the facts every consumer reads (mutation, read-only
    ///     admission, the long timeout, the retry class) and its contract fingerprint.
    /// </summary>
    /// <remarks>
    ///     The fingerprint is the first 12 characters of the base64url SHA-256 of a canonical
    ///     description of the endpoint's request and response JSON contracts (every member's JSON
    ///     name, kind and nullability, and every enum's values, ordered by name, transitively,
    ///     children and related projections included) together with the stack's and the action's
    ///     contract revisions. It changes when either shape or either revision changes, and is the
    ///     same on every platform and in every process that composes the same distribution.
    /// </remarks>
    /// <param name="Template">The route's template: <c>POST /{tenantId}/api/web/centers/query</c>.</param>
    /// <param name="Operation">A standard operation (<c>query</c>, <c>save</c>) or the action's whole name (<c>export/start</c>).</param>
    /// <param name="Resource">The stack's or child query surface's entity name; null on a routed service.</param>
    /// <param name="Action">The descriptor serving the route; null for an operation the pipeline serves directly (<c>query</c>, <c>get</c>, <c>save</c>, <c>delete</c>, …).</param>
    /// <param name="RequestType">The body's type; null when the route takes none.</param>
    /// <param name="ResponseType">The result's type on the wire; <c>typeof(void)</c> when the route answers no body.</param>
    /// <param name="Securable">The securable the route evaluates; null for a member-only route.</param>
    /// <param name="IsMutation">Whether the route writes tenant data.</param>
    /// <param name="AllowWhenReadOnly">Whether a read-only tenant still admits the mutation.</param>
    /// <param name="Long">Whether the route runs under the long request timeout.</param>
    /// <param name="Idempotent">Whether the route is in the retried class: a standard read, or an action declared idempotent.</param>
    /// <param name="Fingerprint">The route's contract fingerprint: 12 base64url characters.</param>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Long is the contract's established name, shared with the action descriptors and the client's contracts metadata.")]
    public sealed record RouteContract(
        string Template,
        string Operation,
        string? Resource,
        ActionDescriptor? Action,
        Type? RequestType,
        Type ResponseType,
        SecurableRef? Securable,
        bool IsMutation,
        bool AllowWhenReadOnly,
        bool Long,
        bool Idempotent,
        string Fingerprint);
}
