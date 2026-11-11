// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Anchors a multilingual text group on a string property <c>P</c>: its twins <c>P2</c> and
    ///     <c>P3</c>, found by name, hold the same text in the tenant's second and third content
    ///     languages.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The twins must exist as nullable strings with the anchor's maximum length and unicode;
    ///         the platform's conventions give a twin that declares neither the anchor's facets. The
    ///         query schema declares a twin only when the tenant has that many content languages, and
    ///         the wire omits an absent twin.
    ///     </para>
    ///     <para>A twin without an anchor is an ordinary column: <c>Address2</c> is never mistaken for a translation.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class MultilingualAttribute : Attribute
    {
        /// <summary>The suffix of the second-language twin.</summary>
        public const string SecondarySuffix = "2";

        /// <summary>The suffix of the third-language twin.</summary>
        public const string TertiarySuffix = "3";
    }
}
