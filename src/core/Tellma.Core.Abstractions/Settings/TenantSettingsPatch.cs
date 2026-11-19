// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     A change to the tenant's settings, under field-mask semantics: <see cref="Fields" /> lists
    ///     what the request sets, and nothing else is touched.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A listed typed member whose value is null clears it, which is how a secondary language
    ///         is removed. A listed entry whose value is null (or absent from <see cref="Entries" />)
    ///         resets the key to its default and deletes its row.
    ///     </para>
    ///     <para>
    ///         Stamps are per row, so two administrators editing different keys never conflict: the
    ///         typed row's stamp is required only when a typed member is listed, and each listed entry
    ///         carries its own.
    ///     </para>
    /// </remarks>
    /// <param name="Fields">
    ///     The members set: typed members by name (<c>Name2</c>, <c>PrimaryLanguage</c>), and entries
    ///     as <c>entries.</c> followed by the key (<c>entries.gl.posting.autoNumberOnPost</c>).
    /// </param>
    /// <param name="Name">The tenant's name in its primary language.</param>
    /// <param name="Name2">The tenant's name in its secondary language.</param>
    /// <param name="Name3">The tenant's name in its tertiary language.</param>
    /// <param name="PrimaryLanguage">The primary content language.</param>
    /// <param name="SecondaryLanguage">The secondary content language.</param>
    /// <param name="TertiaryLanguage">The tertiary content language.</param>
    /// <param name="PrimaryCalendar">The primary calendar's code.</param>
    /// <param name="SecondaryCalendar">The secondary calendar's code.</param>
    /// <param name="TimeZone">The tenant's zone, an IANA id.</param>
    /// <param name="Region">The tenant's default region.</param>
    /// <param name="Digits">The tenant's default digits.</param>
    /// <param name="Entries">The listed entries' values, by key; null when no entry is listed.</param>
    /// <param name="ExpectedModifiedAt">The typed row's stamp as read, required under <c>Check</c> when a typed member is listed.</param>
    /// <param name="ExpectedEntryStamps">
    ///     Each listed entry's stamp as read, by key: the row's stamp, or null when no row was read.
    ///     Under <c>Check</c> every listed entry needs one.
    /// </param>
    /// <param name="Concurrency">Whether the stamps are compared; <c>Override</c> skips every comparison.</param>
    public sealed record TenantSettingsPatch(
        IReadOnlyList<string> Fields,
        string? Name = null,
        string? Name2 = null,
        string? Name3 = null,
        string? PrimaryLanguage = null,
        string? SecondaryLanguage = null,
        string? TertiaryLanguage = null,
        string? PrimaryCalendar = null,
        string? SecondaryCalendar = null,
        string? TimeZone = null,
        string? Region = null,
        DigitStyle? Digits = null,
        IReadOnlyDictionary<string, JsonElement?>? Entries = null,
        DateTimeOffset? ExpectedModifiedAt = null,
        IReadOnlyDictionary<string, DateTimeOffset?>? ExpectedEntryStamps = null,
        ConcurrencyMode Concurrency = ConcurrencyMode.Check)
    {
        /// <summary>The prefix of an entry's field: <c>entries.</c> followed by the key.</summary>
        public const string EntryFieldPrefix = "entries.";
    }
}
