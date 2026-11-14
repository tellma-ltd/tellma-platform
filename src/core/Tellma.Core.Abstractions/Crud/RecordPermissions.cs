// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     One visible row's entry in the platform's <c>permissions</c> details extra: which of the
    ///     caller's granted abilities the row refuses, and why, so a page disables an edit, a delete or
    ///     an action with its reason before the attempt.
    /// </summary>
    /// <remarks>
    ///     An ability is <c>save</c>, <c>delete</c>, <c>activate</c> (both directions) or
    ///     <c>action:&lt;name&gt;</c>. A reason is <c>outside-filter</c> when the grant's filter misses
    ///     the row, else the resource key of the business rule that refuses it. The extra lists one
    ///     entry per visible id, in request order.
    /// </remarks>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="Id">The row's id, written as the entity's own id is.</param>
    /// <param name="Refused">The refused abilities, each with its reason; empty when the row refuses nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Refused" /> is null.</exception>
    public sealed record RecordPermissions<TKey>(TKey Id, IReadOnlyDictionary<string, string> Refused)
        where TKey : struct
    {
        /// <inheritdoc cref="RecordPermissions{TKey}(TKey, IReadOnlyDictionary{string, string})" />
        public IReadOnlyDictionary<string, string> Refused { get; init => field = value ?? throw new ArgumentNullException(nameof(Refused)); } =
            Refused ?? throw new ArgumentNullException(nameof(Refused));
    }
}
