// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of <c>access/holders</c>: who holds one securable, and through which grants.</summary>
    /// <remarks>Both members are required: a body that omits one or sends null is refused as a binding error.</remarks>
    /// <param name="Resource">The securable's resource, such as <c>gl.Center</c>; never null.</param>
    /// <param name="Action">The securable's action, such as <c>Save</c>; never null.</param>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="Resource" /> or <paramref name="Action" /> is null.</exception>
    public sealed record AccessHoldersRequest(string Resource, string Action)
    {
        /// <inheritdoc cref="AccessHoldersRequest(string, string)" />
        public string Resource { get; } = WireMembers.Required(Resource, "resource");

        /// <inheritdoc cref="AccessHoldersRequest(string, string)" />
        public string Action { get; } = WireMembers.Required(Action, "action");
    }
}
