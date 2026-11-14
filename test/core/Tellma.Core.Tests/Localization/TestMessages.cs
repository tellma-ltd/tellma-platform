// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The marker of the messages the localizer tests render, <c>TestMessages.resx</c> (neutral
    ///     English) with its <c>ar</c>, <c>ar-SA</c> and <c>am</c> satellites, addressed the way a
    ///     feature addresses its own resources: by the marker's type.
    /// </summary>
    /// <remarks>
    ///     The keys <c>Level</c>, <c>LanguageLevel</c> and <c>NeutralLevel</c> pin the fallback
    ///     chain: <c>Level</c> exists at every level, <c>LanguageLevel</c> in Arabic and the neutral
    ///     resources, <c>NeutralLevel</c> only in the neutral ones; each value names its level.
    /// </remarks>
    internal sealed class TestMessages
    {
        /// <summary>Not instantiable: the type only marks the resources.</summary>
        private TestMessages()
        {
        }
    }
}
