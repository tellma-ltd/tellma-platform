// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     A type as a signature declares it: a concrete type, a set, or a variable over a set.
    /// </summary>
    /// <remarks>
    ///     One shape covers all three, which is what lets a signature say "the result has the same
    ///     type as the first argument" without any machinery beyond naming the same variable twice.
    /// </remarks>
    internal sealed record TypeSpec
    {
        /// <summary>
        ///     The variable's name within its signature, or null for a plain type or set. Variables
        ///     bind on first use and are shared across every position that names them.
        /// </summary>
        internal string? Variable { get; init; }

        /// <summary>The concrete type, or the set the variable ranges over.</summary>
        internal required TypeMask Admits { get; init; }

        /// <summary>
        ///     Whether an argument here has to have had its zone resolved already.
        /// </summary>
        /// <remarks>
        ///     Set on the calendar positions. An offset-carrying value there gets a diagnostic that
        ///     names the fix, rather than the generic complaint that no overload matched — the
        ///     mistake is specific and common enough to deserve its own answer.
        /// </remarks>
        internal bool RequiresZoneResolution { get; init; }

        /// <summary>
        ///     When set, the result type is read from the literal selector at this argument position
        ///     rather than fixed by the signature.
        /// </summary>
        internal int? FromSelectorAt { get; init; }
    }

    /// <summary>The named type shapes the function library is written in terms of.</summary>
    internal static class TypeSpecs
    {
        /// <summary>A truth value.</summary>
        internal static TypeSpec Bool { get; } = new TypeSpec { Admits = TypeMask.Bool };

        /// <summary>An exact decimal.</summary>
        internal static TypeSpec Numeric { get; } = new TypeSpec { Admits = TypeMask.Numeric };

        /// <summary>Unicode text.</summary>
        internal static TypeSpec Text { get; } = new TypeSpec { Admits = TypeMask.String };

        /// <summary>A calendar date.</summary>
        internal static TypeSpec Date { get; } = new TypeSpec { Admits = TypeMask.Date };

        /// <summary>A date and time without an offset.</summary>
        internal static TypeSpec LocalDateTime { get; } = new TypeSpec { Admits = TypeMask.DateTime };

        /// <summary>An instant with a UTC offset.</summary>
        internal static TypeSpec OffsetDateTime { get; } = new TypeSpec { Admits = TypeMask.DateTimeOffset };

        /// <summary>
        ///     A time of day: the one date type that carries both a clock reading and a resolved
        ///     zone, so extracting an hour from it means something.
        /// </summary>
        internal static TypeSpec TimeOfDay { get; } = new TypeSpec
        {
            Admits = TypeMask.TimeOfDay,
            RequiresZoneResolution = true,
        };

        /// <summary>A literal selector, whose text is consumed at compile time.</summary>
        internal static TypeSpec Selector { get; } = new TypeSpec { Admits = TypeMask.String };

        /// <summary>A variable over every type.</summary>
        /// <param name="variable">The variable's name within its signature.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec Any(string variable)
        {
            return new TypeSpec { Variable = variable, Admits = TypeMask.Any };
        }

        /// <summary>A variable over the ordered types.</summary>
        /// <param name="variable">The variable's name within its signature.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec Ordered(string variable)
        {
            return new TypeSpec { Variable = variable, Admits = TypeMask.Ordered };
        }

        /// <summary>
        ///     A variable over the three date types, for elapsed-time operations but a sub-day shift.
        /// </summary>
        /// <param name="variable">The variable's name within its signature.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec Instant(string variable)
        {
            return new TypeSpec { Variable = variable, Admits = TypeMask.Instant };
        }

        /// <summary>
        ///     A variable over the date types a sub-day shift can be added to.
        /// </summary>
        /// <param name="variable">The variable's name within its signature.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec SubDayInstant(string variable)
        {
            return new TypeSpec { Variable = variable, Admits = TypeMask.SubDay };
        }

        /// <summary>
        ///     A variable over the zone-resolved date types, for calendar operations.
        /// </summary>
        /// <param name="variable">The variable's name within its signature.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec Calendar(string variable)
        {
            return new TypeSpec
            {
                Variable = variable,
                Admits = TypeMask.Calendar,
                RequiresZoneResolution = true,
            };
        }

        /// <summary>A result type read from a literal selector argument.</summary>
        /// <param name="selectorIndex">Which argument names the type.</param>
        /// <param name="admits">The types that selector can name.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec FromSelector(int selectorIndex, TypeMask admits)
        {
            return new TypeSpec { Admits = admits, FromSelectorAt = selectorIndex };
        }

        /// <summary>A concrete type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The specification.</returns>
        internal static TypeSpec Of(BoundType type)
        {
            return new TypeSpec { Admits = BoundTypes.Mask(type) };
        }
    }
}
