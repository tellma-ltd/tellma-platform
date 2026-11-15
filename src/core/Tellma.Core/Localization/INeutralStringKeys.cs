// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The keys the composed features' neutral (English) shared string resources declare, which
    ///     decide the first key of a label chain the client metadata emits.
    /// </summary>
    /// <remarks>
    ///     A singleton: the string pack composer, which answers from the English dictionary it
    ///     merged from every composed feature assembly's neutral <c>Strings</c> resources.
    /// </remarks>
    internal interface INeutralStringKeys
    {
        /// <summary>Whether a neutral shared string resource declares a key, compared ordinally.</summary>
        /// <param name="key">The key.</param>
        /// <returns>True when one does.</returns>
        bool Declares(string key);
    }
}
