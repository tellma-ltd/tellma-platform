// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>Another user's preference bag, as an administrator reads or writes it.</summary>
    /// <param name="Preferences">Every key the bag holds, with its value.</param>
    public sealed record UserPreferencesResult(IReadOnlyDictionary<string, string> Preferences);
}
