// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     Which conversions exist, and how each is emitted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The set is closed and dialect-free: which conversions the language offers is decided
    ///         here, and a conversion nobody declared is a bind-time diagnostic rather than
    ///         something the backend gets asked about.
    ///     </para>
    ///     <para>
    ///         Every emission uses the failing conversion, never the forgiving one. A conversion
    ///         that silently yields an absent value on failure would falsify the nullity analysis,
    ///         and the guards the emitter omits on the strength of that analysis are what make
    ///         comparison total.
    ///     </para>
    ///     <para>
    ///         Rendering to text is culture-invariant and stable across releases: dates come out in
    ///         the international format, numbers with their own scale and no group separators,
    ///         truth values as words, and identifiers in lowercase. Reading text back carries an
    ///         explicit, language-neutral format, so no session setting can change what a stored
    ///         expression means.
    ///     </para>
    /// </remarks>
    internal static class CastRules
    {
        /// <summary>The name each target type goes by in an expression.</summary>
        private static readonly (string Name, BoundType Type)[] Targets =
        [
            ("numeric", BoundType.Numeric),
            ("string", BoundType.String),
            ("bool", BoundType.Bool),
            ("guid", BoundType.Guid),
            ("date", BoundType.Date),
            ("datetime", BoundType.DateTime),
            ("datetimeoffset", BoundType.DateTimeOffset),
        ];

        /// <summary>The target types by name.</summary>
        private static readonly FrozenDictionary<string, BoundType> TargetsByName =
            Targets.ToFrozenDictionary(
                static target => target.Name,
                static target => target.Type,
                StringComparer.OrdinalIgnoreCase);

        /// <summary>The emission for each supported conversion.</summary>
        private static readonly FrozenDictionary<(BoundType From, BoundType To), EmitTemplate> Templates =
            BuildTemplates();

        /// <summary>The names a conversion may target.</summary>
        internal static string[] TargetNames => [.. Targets.Select(static target => target.Name)];

        /// <summary>Resolves a target name.</summary>
        /// <param name="name">The name as written.</param>
        /// <param name="type">The target type, when the name is one.</param>
        /// <returns>True when the name names a target.</returns>
        internal static bool TryResolveTarget(string name, out BoundType type)
        {
            return TargetsByName.TryGetValue(name, out type);
        }

        /// <summary>Whether a conversion exists.</summary>
        /// <param name="from">The source type.</param>
        /// <param name="to">The target type.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     An absent-value literal converts to everything, which is the idiom that gives a bare
        ///     absent value a column type. Converting a type to itself is allowed and emits nothing,
        ///     which costs nothing and is what a generated expression naturally produces.
        /// </remarks>
        internal static bool IsSupported(BoundType from, BoundType to)
        {
            return from == BoundType.Null || from == to || Templates.ContainsKey((from, to));
        }

        /// <summary>The emission for a conversion.</summary>
        /// <param name="from">The source type.</param>
        /// <param name="to">The target type.</param>
        /// <returns>The template, or null when the source and target are the same.</returns>
        internal static EmitTemplate? TemplateFor(BoundType from, BoundType to)
        {
            return from == to ? null : Templates.GetValueOrDefault((from, to));
        }

        /// <summary>Builds the conversion table.</summary>
        /// <returns>The table.</returns>
        private static FrozenDictionary<(BoundType From, BoundType To), EmitTemplate> BuildTemplates()
        {
            Dictionary<(BoundType From, BoundType To), (string Pattern, QueryexStoreType Result)> patterns = new()
            {
                // Rendering to text. The widths are the longest each form can produce, so nothing is
                // ever silently clipped.
                [(BoundType.Numeric, BoundType.String)] = ("CONVERT(nvarchar(4000), {0})", QueryexStoreType.QxNVarChar(4000)),
                [(BoundType.Guid, BoundType.String)] = ("LOWER(CONVERT(nvarchar(36), {0}))", QueryexStoreType.QxNVarChar(36)),
                [(BoundType.Date, BoundType.String)] = ("CONVERT(nvarchar(10), {0}, 23)", QueryexStoreType.QxNVarChar(10)),
                [(BoundType.DateTime, BoundType.String)] = ("CONVERT(nvarchar(27), {0}, 126)", QueryexStoreType.QxNVarChar(27)),

                // Style 126 rather than the one that normalises to a zero offset: the value is read
                // at its own offset, and normalising would throw that offset away.
                [(BoundType.DateTimeOffset, BoundType.String)] = ("CONVERT(nvarchar(34), {0}, 126)", QueryexStoreType.QxNVarChar(34)),

                // A simple case rather than a searched one, so the argument appears once and an
                // absent value falls through to an absent result instead of to the wrong word.
                [(BoundType.Bool, BoundType.String)] =
                    ("CASE {0} WHEN 1 THEN N'true' WHEN 0 THEN N'false' END", QueryexStoreType.QxNVarChar(5)),

                // Reading text back. Each carries an explicit, language-neutral format, because the
                // format-free conversions read a session setting and would make the same stored
                // expression mean different things on two connections.
                [(BoundType.String, BoundType.Numeric)] = ("CAST({0} AS decimal(38, 6))", QueryexStoreType.QxDecimal(38, 6)),
                [(BoundType.String, BoundType.Guid)] = ("CAST({0} AS uniqueidentifier)", QueryexStoreType.QxUniqueIdentifier),
                [(BoundType.String, BoundType.Date)] = ("CONVERT(date, {0}, 23)", QueryexStoreType.QxDate),
                [(BoundType.String, BoundType.DateTime)] = ("CONVERT(datetime2(7), {0}, 126)", QueryexStoreType.QxDateTime2(7)),
                [(BoundType.String, BoundType.DateTimeOffset)] =
                    ("CONVERT(datetimeoffset(7), {0}, 127)", QueryexStoreType.QxDateTimeOffset(7)),

                // Between truth values and numbers.
                [(BoundType.Numeric, BoundType.Bool)] = ("CAST({0} AS bit)", QueryexStoreType.QxBit),
                [(BoundType.Bool, BoundType.Numeric)] = ("CAST({0} AS int)", QueryexStoreType.QxInt),

                // Between the date types. An offset-carrying instant is read at its own offset;
                // reading it in a named zone is what the zone function is for.
                [(BoundType.Date, BoundType.DateTime)] = ("CAST({0} AS datetime2(7))", QueryexStoreType.QxDateTime2(7)),
                [(BoundType.DateTime, BoundType.Date)] = ("CONVERT(date, {0})", QueryexStoreType.QxDate),
                [(BoundType.DateTimeOffset, BoundType.Date)] = ("CONVERT(date, {0})", QueryexStoreType.QxDate),
                [(BoundType.DateTimeOffset, BoundType.DateTime)] = ("CAST({0} AS datetime2(7))", QueryexStoreType.QxDateTime2(7)),
            };

            Dictionary<(BoundType From, BoundType To), EmitTemplate> templates = [];
            foreach (((BoundType from, BoundType to), (string pattern, QueryexStoreType result)) in patterns)
            {
                templates[(from, to)] = EmitTemplate.Parse(pattern, result);
            }

            return templates.ToFrozenDictionary();
        }
    }
}
