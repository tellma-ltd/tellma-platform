// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One kind of notification a composition can raise, declared once by the feature whose code
    ///     raises it, through the feature's contribution. The client metadata lists every registered
    ///     type, so the client renders a type's rows without asking.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Key" /> follows the grammar <c>^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$</c>, at
    ///         most 64 characters. <c>core.</c> is the platform's; a module uses its slug
    ///         (<c>gl.</c>) and a distribution its deployment slug (<c>acme.document.approved</c>).
    ///         Keys are persisted and stable for the life of a distribution; a changed meaning is a
    ///         new key.
    ///     </para>
    ///     <para>
    ///         <see cref="Category" /> follows <c>^[a-z][a-z0-9-]*$</c>, at most 32 characters; its
    ///         label is the resource <c>NotificationCategory_&lt;Category&gt;</c>. A channel-only type
    ///         (<see cref="Inbox" /> false) is neither <see cref="Mutable" /> nor a
    ///         <see cref="Toast" />. The composition refuses at startup a key declared twice or a type
    ///         that breaks these rules.
    ///     </para>
    /// </remarks>
    /// <param name="Key">The type's key, stored as each row's type.</param>
    /// <param name="Category">The group the type belongs to in the user's notification settings (<c>jobs</c>, <c>files</c>, <c>users</c>).</param>
    /// <param name="Mutable">
    ///     Whether the user may turn the inbox off for the type. Every other channel is always the
    ///     user's choice.
    /// </param>
    /// <param name="TargetResource">
    ///     The entity every notification of the type points at, which the client maps to a route; each
    ///     request then carries a target id. Null when the type points at nothing.
    /// </param>
    /// <param name="Severity">The default severity of the type's rows; a request may override it.</param>
    /// <param name="Toast">Whether a row arriving while the app is open pops a toast.</param>
    /// <param name="Inbox">
    ///     False for a channel-only type: no inbox row is written, and a request only reaches the
    ///     other channels.
    /// </param>
    public sealed record NotificationTypeDescriptor(
        string Key,
        string Category,
        bool Mutable,
        string? TargetResource,
        NotificationSeverity Severity = NotificationSeverity.Info,
        bool Toast = false,
        bool Inbox = true);
}
