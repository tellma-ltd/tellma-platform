// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The one server renderer of display names: the one string that stands for a row of an entity in
    ///     a spreadsheet's key column, a report member, an agent tool's result or a notification's actor.
    ///     A singleton. The client renders the same rule from the same columns.
    /// </summary>
    /// <remarks>
    ///     A display name renders the entity's display-name source (<see cref="EntityMetadata.DisplayName" />):
    ///     a group's value in the active content slot, else its first slot's; a scalar formatted with
    ///     ungrouped Latin digits and dates in the calendar's numeric style. A prefix and a name join with
    ///     <see cref="DisplayNameMetadata.Separator" />; with neither, the row renders by its id. The text
    ///     carries no bidirectional control characters.
    /// </remarks>
    public interface IDisplayNames
    {
        /// <summary>
        ///     The columns a caller selects to render an entity's display names: <c>Id</c>, then the
        ///     display-name columns the tenant's content languages admit, each prefixed by
        ///     <paramref name="navigationPath" /> and a dot when one is given.
        /// </summary>
        /// <param name="entity">The entity's name, such as <c>gl.Center</c>.</param>
        /// <param name="shape">The tenant's content languages, which decide the group twins selected.</param>
        /// <param name="navigationPath">The navigation that reaches the entity, such as <c>CreatedBy</c>; null for the root.</param>
        /// <returns>The column paths, in the order <see cref="Render" /> takes their values.</returns>
        IReadOnlyList<string> Columns(string entity, MultilingualShape shape, string? navigationPath = null);

        /// <summary>Renders one row's display name as plain text.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <param name="values">One value per entry of <see cref="Columns" />, in that order.</param>
        /// <param name="contentSlot">The active content language's slot: 1, 2 or 3.</param>
        /// <param name="culture">The culture numbers and dates are formatted in.</param>
        /// <param name="calendar">The calendar dates are rendered in.</param>
        /// <returns>The display name.</returns>
        string Render(string entity, IReadOnlyList<object?> values, int contentSlot, CultureInfo culture, ICalendarSystem calendar);
    }
}
