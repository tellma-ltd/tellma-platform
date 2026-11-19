// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     One action an entity's API offers beyond its operations, as its <see cref="ActionDescriptor" />
    ///     declares it, with the key of its label resolved and its arguments described for the generated
    ///     dialog.
    /// </summary>
    /// <param name="Name">The action's name, such as <c>activate</c> or <c>preferences/set</c>.</param>
    /// <param name="Kind">Whether the action runs on records or is an API call of its own.</param>
    /// <param name="BuiltIn">Whether the platform defines the action, as it does activation.</param>
    /// <param name="Action">The securable action that gates it; null, written as such, when any member may run it.</param>
    /// <param name="LabelKey">The resource key of the action's label.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    /// <param name="Mutation">Whether the action writes.</param>
    /// <param name="Idempotent">Whether a retry of the action is safe.</param>
    /// <param name="Destructive">Whether the action asks for confirmation before it runs.</param>
    /// <param name="AllowWhenReadOnly">Whether the action runs on a read-only tenant; omitted when false.</param>
    /// <param name="SingleTarget">Whether the action takes exactly one record.</param>
    /// <param name="SupportsFilter">Whether the action accepts a filter in place of ids.</param>
    /// <param name="Result">What the action answers.</param>
    /// <param name="Arguments">The JSON Schema (draft 2020-12) of the action's arguments or body; null, and omitted, when it takes none.</param>
    /// <param name="Fields">The action's arguments, one per top-level property; null, and omitted, when it takes none.</param>
    public sealed record ClientActionMetadata(
        string Name,
        ClientActionKind Kind,
        bool BuiltIn,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Action,
        string LabelKey,
        string? LabelFallback,
        bool Mutation,
        bool Idempotent,
        bool Destructive,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AllowWhenReadOnly,
        bool SingleTarget,
        bool SupportsFilter,
        ClientActionResult Result,
        JsonElement? Arguments,
        IReadOnlyList<ClientActionField>? Fields);
}
