// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>One preference of a user's preference bag: a dotted key and its value.</summary>
    /// <param name="Key">The key, such as <c>nav.pinned</c>: dotted segments, the first led by a lower-case letter.</param>
    /// <param name="Value">The value, opaque to the server; typically JSON the client wrote.</param>
    public sealed record KeyValueItem(string Key, string Value);
}
