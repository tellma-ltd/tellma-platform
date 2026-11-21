// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of <c>activate</c>, <c>deactivate</c> and every entity action that answers its
    ///     target rows read back and takes no arguments.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ids" /> follows the rules of <see cref="IdsRequest.Ids" />: a body that omits it,
    ///     or sends null, binds an empty list.
    /// </remarks>
    public class EntityActionRequest
    {
        /// <summary>The target rows' ids; numbers or strings on the wire, narrowed to the stack's key type. Never null.</summary>
        public IReadOnlyList<long> Ids
        {
            get;
            set => field = value ?? [];
        } = [];

        /// <summary>Whether the answer carries the target rows read back in details shape; true by default.</summary>
        public bool ReturnEntities { get; set; } = true;

        /// <summary>The details extras to include with the rows read back, by name; null or empty for none.</summary>
        public IReadOnlyList<string>? Include { get; set; }
    }

    /// <summary>The body of an entity action that answers its target rows read back and takes arguments.</summary>
    /// <remarks>
    ///     <see cref="Arguments" /> is required: a body that omits it fails the serializer's
    ///     required-member check, and one that sends null is refused by the setter, both as binding
    ///     errors. An error in the arguments is rooted at <c>arguments</c> (<c>arguments.creditLimit</c>).
    /// </remarks>
    /// <typeparam name="TArguments">The action's arguments record.</typeparam>
    public sealed class EntityActionRequest<TArguments> : EntityActionRequest
    {
        /// <summary>The action's arguments; never null.</summary>
        /// <exception cref="System.Text.Json.JsonException">The value set is null.</exception>
        public required TArguments Arguments
        {
            get;
            set => field = WireMembers.Required(value, "arguments");
        }
    }
}
