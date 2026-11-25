// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     One telemetry record of a <see cref="ClientTelemetryBatch" />: an event, a timing or an error
    ///     the SPA recorded. It carries closed names and codes, never tenant content or free text.
    /// </summary>
    /// <param name="Kind">What the item records: <c>event</c>, <c>timing</c> or <c>error</c>.</param>
    /// <param name="Name">
    ///     The item's name, <c>&lt;scope&gt;.&lt;area&gt;.&lt;event&gt;[.&lt;more&gt;]</c> in lowercase
    ///     and at most 64 characters, such as <c>tm.api.slow</c>; every error is <c>tm.error.captured</c>.
    /// </param>
    /// <param name="At">The client's clock when the item was recorded.</param>
    /// <param name="TraceId">The trace id of the request the item is about, 32 lowercase hexadecimal digits; null when it is about none.</param>
    /// <param name="Properties">
    ///     The item's properties, at most 16: camelCase keys of at most 32 characters, each a number, a
    ///     boolean or a string. Null for none.
    /// </param>
    public sealed record ClientTelemetryItem(
        string Kind,
        string Name,
        DateTimeOffset At,
        string? TraceId,
        IReadOnlyDictionary<string, JsonElement>? Properties);
}
