// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     The binder's type domain: the language's types plus the two the binder needs internally.
    /// </summary>
    /// <remarks>
    ///     Deliberately laid out so the first nine members line up one-for-one with the public type
    ///     enum; a test pins that, because a silent reorder would mistranslate every result.
    /// </remarks>
    internal enum BoundType
    {
        /// <summary>A truth value.</summary>
        Bool = 0,

        /// <summary>An exact decimal.</summary>
        Numeric = 1,

        /// <summary>Unicode text.</summary>
        String = 2,

        /// <summary>A globally unique identifier.</summary>
        Guid = 3,

        /// <summary>A calendar date.</summary>
        Date = 4,

        /// <summary>A date and time without an offset.</summary>
        DateTime = 5,

        /// <summary>An instant with a UTC offset.</summary>
        DateTimeOffset = 6,

        /// <summary>A node in a hierarchy.</summary>
        HierarchyId = 7,

        /// <summary>A spatial value.</summary>
        Geography = 8,

        /// <summary>
        ///     The type of the absent-value literal alone. Never reaches a public result: an
        ///     expression whose only possible value is absent takes the type its context demands,
        ///     and carries its absence as a nullity instead.
        /// </summary>
        Null = 9,

        /// <summary>
        ///     The type of a node that failed to bind. Every judgement involving it succeeds
        ///     silently and yields it again, which is what stops one real mistake from producing a
        ///     cascade of complaints about everything built on top of it.
        /// </summary>
        Error = 10,
    }

    /// <summary>A set of types as one value: the bound of a type variable, or an accepted set.</summary>
    [Flags]
    internal enum TypeMask
    {
        /// <summary>No type.</summary>
        None = 0,

        /// <summary>A truth value.</summary>
        Bool = 1 << 0,

        /// <summary>An exact decimal.</summary>
        Numeric = 1 << 1,

        /// <summary>Unicode text.</summary>
        String = 1 << 2,

        /// <summary>A globally unique identifier.</summary>
        Guid = 1 << 3,

        /// <summary>A calendar date.</summary>
        Date = 1 << 4,

        /// <summary>A date and time without an offset.</summary>
        DateTime = 1 << 5,

        /// <summary>An instant with a UTC offset.</summary>
        DateTimeOffset = 1 << 6,

        /// <summary>A node in a hierarchy.</summary>
        HierarchyId = 1 << 7,

        /// <summary>A spatial value.</summary>
        Geography = 1 << 8,

        /// <summary>Every type the language has.</summary>
        Any = Bool | Numeric | String | Guid | Date | DateTime | DateTimeOffset | HierarchyId | Geography,

        /// <summary>Every type that can be compared for equality, grouped, or tested for membership.</summary>
        Equatable = Any & ~Geography,

        /// <summary>Every type with a meaningful order.</summary>
        Ordered = Any & ~Geography & ~Guid,

        /// <summary>The three date types, which every elapsed-time operation accepts.</summary>
        Instant = Date | DateTime | DateTimeOffset,

        /// <summary>
        ///     The date types a calendar operation accepts: the ones already resolved to a zone.
        /// </summary>
        Calendar = Date | DateTime,

        /// <summary>The one date type that carries a time of day and a resolved zone.</summary>
        TimeOfDay = DateTime,
    }

    /// <summary>Translations between the binder's type domain and the language's.</summary>
    internal static class BoundTypes
    {
        /// <summary>The public type a bound type corresponds to.</summary>
        /// <param name="type">The bound type, which must be one of the language's own.</param>
        /// <returns>The public type.</returns>
        internal static QueryexType ToPublic(BoundType type)
        {
            return type is BoundType.Null or BoundType.Error
                ? throw new InvalidOperationException("An internal-only type has no public counterpart.")
                : (QueryexType)type;
        }

        /// <summary>The bound type a public type corresponds to.</summary>
        /// <param name="type">The public type.</param>
        /// <returns>The bound type.</returns>
        internal static BoundType FromPublic(QueryexType type)
        {
            return (BoundType)type;
        }

        /// <summary>The one-type mask for a bound type.</summary>
        /// <param name="type">The bound type.</param>
        /// <returns>The mask, or none for the internal-only types.</returns>
        internal static TypeMask Mask(BoundType type)
        {
            return type is BoundType.Null or BoundType.Error
                ? TypeMask.None
                : (TypeMask)(1 << (int)type);
        }

        /// <summary>Whether a mask admits a type.</summary>
        /// <param name="mask">The mask.</param>
        /// <param name="type">The type.</param>
        /// <returns>True when the mask admits it.</returns>
        internal static bool Admits(TypeMask mask, BoundType type)
        {
            return (mask & Mask(type)) != TypeMask.None;
        }

        /// <summary>The single type a mask admits, when it admits exactly one.</summary>
        /// <param name="mask">The mask.</param>
        /// <param name="type">The type, when there is exactly one.</param>
        /// <returns>True when the mask admits exactly one type.</returns>
        internal static bool TrySingle(TypeMask mask, out BoundType type)
        {
            type = BoundType.Error;
            if (mask == TypeMask.None || (mask & (mask - 1)) != TypeMask.None)
            {
                return false;
            }

            type = (BoundType)BitOperations.TrailingZeroCount((uint)mask);
            return true;
        }

        /// <summary>The name of a type, for a diagnostic's arguments.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name.</returns>
        internal static string Name(BoundType type)
        {
            return type.ToString();
        }

        /// <summary>The names of every type a mask admits, comma-separated.</summary>
        /// <param name="mask">The mask.</param>
        /// <returns>The names.</returns>
        internal static string Names(TypeMask mask)
        {
            List<string> names = [];
            for (int index = 0; index <= (int)BoundType.Geography; index++)
            {
                var candidate = (BoundType)index;
                if (Admits(mask, candidate))
                {
                    names.Add(Name(candidate));
                }
            }

            return string.Join(", ", names);
        }
    }
}
