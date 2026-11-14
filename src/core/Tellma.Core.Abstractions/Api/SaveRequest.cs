// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of a stack's <c>save</c>: one or more entities, inserts and updates together.</summary>
    /// <remarks>
    ///     <see cref="Entities" /> is required: a body that omits it fails the serializer's
    ///     required-member check, and one that sends null is refused by the setter, both as binding
    ///     errors. It holds at least one entity and at most the surface's save ceiling, else a
    ///     limit-exceeded error. An error on a row binds as <c>entities[i]</c>.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class SaveRequest<TEntity>
    {
        /// <summary>The entities to save; never null.</summary>
        /// <exception cref="System.Text.Json.JsonException">The value set is null.</exception>
        public required IReadOnlyList<TEntity> Entities
        {
            get;
            set => field = WireMembers.Required(value, "entities");
        }

        /// <summary>Whether the answer carries the saved rows read back in details shape; true by default.</summary>
        public bool ReturnEntities { get; set; } = true;

        /// <summary>The details extras to include with the rows read back, by name; null or empty for none.</summary>
        public IReadOnlyList<string>? Include { get; set; }

        /// <summary>
        ///     Whether an update's stamp is compared: the details page sends
        ///     <see cref="ConcurrencyMode.Check" />, the default, then
        ///     <see cref="ConcurrencyMode.Override" /> once the user chooses to overwrite.
        /// </summary>
        public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Check;
    }
}
