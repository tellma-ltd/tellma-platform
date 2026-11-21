// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Resources;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Resources
{
    /// <summary>
    ///     The marker of Core's shared resources, <c>Resources/Strings.resx</c> (neutral English) and
    ///     its satellites <c>Strings.&lt;language&gt;.resx</c>. Every key in them also reaches the
    ///     browser application through the string pack, so text only the server renders belongs in a
    ///     separate file.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Keys are <c>PascalCase_Underscored</c>. A month name of a calendar the platform renders
    ///         itself is <c>Calendar_&lt;code&gt;_Month&lt;n&gt;</c>, taken from CLDR so the server and
    ///         the browser agree. A region's name is <c>Region_&lt;code&gt;</c>, generated from CLDR
    ///         for every region of the language catalogue in every shipped language by
    ///         <c>eng/region-names</c>, never edited by hand. A problem's title and detail are
    ///         <c>Problem_&lt;code&gt;</c> and <c>Problem_&lt;code&gt;_Detail</c> with <c>-</c> written
    ///         <c>_</c>; a validation code is its own key with <c>.</c> written <c>_</c>.
    ///     </para>
    ///     <para>
    ///         Every value is an ICU message. A message counts with <c>{count, plural, …}</c>, varies by
    ///         gender with <c>{gender, select, female {…} male {…} other {…}}</c> (<c>other</c> is
    ///         mandatory), and names every argument: no positional <c>{0}</c>. A number shown in a
    ///         message is written <c>{n, number}</c>, so it is grouped. An apostrophe is written as the
    ///         typographic <c>’</c>, never <c>'</c>, which ICU reads as a quote.
    ///     </para>
    ///     <para>
    ///         A package outside Core addresses these resources by <see cref="CoreStrings.BaseName" />
    ///         and <see cref="CoreStrings.Assembly" />, never through this type.
    ///     </para>
    /// </remarks>
    internal sealed class Strings
    {
        /// <summary>The resources, looked up by culture through the framework's parent chain to the neutral English set.</summary>
        public static ResourceManager ResourceManager { get; } = new(CoreStrings.BaseName, typeof(Strings).Assembly);

        /// <summary>Not instantiable: the type only marks the resources.</summary>
        private Strings()
        {
        }
    }
}
