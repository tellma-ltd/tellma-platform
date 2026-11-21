// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The caller's own preference bag after a write, with the caller's preferences tag as it
    ///     travels in the version-tags header, so the calling tab adopts both at once.
    /// </summary>
    /// <param name="Preferences">Every key the bag holds, with its value.</param>
    /// <param name="PreferencesTag">The caller's preferences tag after the write, in its wire form.</param>
    public sealed record PreferencesResult(IReadOnlyDictionary<string, string> Preferences, string PreferencesTag);
}
