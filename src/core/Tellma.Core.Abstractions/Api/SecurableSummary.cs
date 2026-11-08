// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     One securable the caller holds, as the <c>me</c> envelope's access summary lists it: the
    ///     pair, and whether a row-level filter narrows it.
    /// </summary>
    /// <param name="Resource">The securable's resource, such as <c>gl.Center</c>.</param>
    /// <param name="Action">The securable's action, such as <c>Save</c>.</param>
    /// <param name="HasFilter">Whether the caller's grant is narrowed by a row-level filter rather than unrestricted.</param>
    public sealed record SecurableSummary(string Resource, string Action, bool HasFilter);
}
