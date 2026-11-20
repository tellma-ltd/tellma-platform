// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Testing.Crud
{
    /// <summary>One permission a conformance caller's role holds: a securable and, for a filtered grant, its filter.</summary>
    /// <param name="Resource">The securable's resource, such as <c>gl.Center</c>.</param>
    /// <param name="Action">The securable's action, such as <c>Read</c>.</param>
    /// <param name="Filter">The grant's Queryex filter over the resource; null for an unfiltered grant.</param>
    public sealed record StackGrant(string Resource, string Action, string? Filter = null);
}
