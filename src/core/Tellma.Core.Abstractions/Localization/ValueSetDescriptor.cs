// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     A value set: a named enumeration whose values come from the composition rather than a C#
    ///     type. It is fixed per deployment and the same for every tenant.
    /// </summary>
    /// <remarks>
    ///     A set may be wider than what a deployment offers: the language and region sets hold every
    ///     value the platform can describe, so a stored value that is no longer offered still renders
    ///     its label. Whether a value may be chosen is a rule of the property's entity, not of the set.
    /// </remarks>
    /// <param name="Name">The set's name: a lower-case schema, a dot and a PascalCase name (<c>core.TimeZone</c>, <c>gl.CostMethod</c>).</param>
    /// <param name="Values">The values, in the order lists show them. Values are unique without regard to case.</param>
    /// <param name="Aliases">
    ///     Other spellings of some values, each mapped to the value it stands for (the older id a
    ///     browser reports for a renamed time zone). An alias is never itself a value, so a save
    ///     refuses it like any other text outside the set; a client maps through the aliases before
    ///     it compares or saves. Null when the set has none.
    /// </param>
    public sealed record ValueSetDescriptor(
        string Name,
        IReadOnlyList<ValueSetEntry> Values,
        IReadOnlyDictionary<string, string>? Aliases = null);
}
