// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The string packs of the composition: one per offered language, and one for English, every
    ///     pack's fallback, whether or not it is offered. Composed once, on first use, and never
    ///     changed.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The contract's established name; providers are written in C#, where Get is not a keyword.")]
    public interface IStringPackProvider
    {
        /// <summary>A language's pack.</summary>
        /// <param name="language">The bare language code (<c>ar</c>), compared without regard to case.</param>
        /// <returns>The pack, or null for a language other than English that the distribution does not offer.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="language" /> is null.</exception>
        StringPack? Get(string language);
    }
}
