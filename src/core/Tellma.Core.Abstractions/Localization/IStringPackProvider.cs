// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>The string packs of the composition: one per offered language, composed once at startup.</summary>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The contract's established name; providers are written in C#, where Get is not a keyword.")]
    public interface IStringPackProvider
    {
        /// <summary>An offered language's pack.</summary>
        /// <param name="language">The language code.</param>
        /// <returns>The pack, or null for a language the distribution does not offer.</returns>
        StringPack? Get(string language);
    }
}
