// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // The binding rules are spec 0012 §11.3's.
    /// <summary>
    ///     Binds a compiled query's parameter slots: literal slots from the compilation, the context
    ///     slots from the batch's context values, and declared slots from the caller's arguments.
    /// </summary>
    internal static class QueryexSlots
    {
        /// <summary>The widest a Unicode parameter is declared before it is declared unbounded.</summary>
        private const int UnicodeBound = 4000;

        /// <summary>The widest a non-Unicode parameter is declared before it is declared unbounded.</summary>
        private const int NarrowBound = 8000;

        /// <summary>The parameters of every slot of a compiled query, in slot order.</summary>
        /// <param name="compiled">The compiled query.</param>
        /// <param name="context">The context values.</param>
        /// <param name="values">The declared parameters' values, by name, ignoring case.</param>
        /// <returns>The parameters.</returns>
        /// <exception cref="InvalidOperationException">A declared slot names a parameter the arguments give no value.</exception>
        public static IReadOnlyList<ScalarParameter> Parameters(CompiledQuery compiled, QueryexContextValues context, IReadOnlyDictionary<string, object?> values)
        {
            ArgumentNullException.ThrowIfNull(compiled);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(values);

            List<ScalarParameter> parameters = new(compiled.Parameters.Count);
            foreach (QueryexParameterSlot slot in compiled.Parameters)
            {
                // One value per origin; a declared parameter bound against several store types fills
                // every one of its slots from the one value.
                object? value = slot.Origin switch
                {
                    QueryexParameterOrigin.Literal => slot.Value,
                    QueryexParameterOrigin.Today => context.Today,
                    QueryexParameterOrigin.Now => context.Now,
                    QueryexParameterOrigin.UserId => context.UserId,
                    QueryexParameterOrigin.TimeZone => context.TimeZoneName,
                    QueryexParameterOrigin.Declared => slot.DeclaredName is string name && values.TryGetValue(name, out object? supplied)
                        ? supplied
                        : throw new InvalidOperationException(
                            $"The compiled query binds the parameter '{slot.DeclaredName}', which the arguments give no value. A parameter is either bound to a value or omitted."),
                    _ => throw new InvalidOperationException($"The slot {slot.Name} has an origin no host binds, {slot.Origin}."),
                };

                parameters.Add(Describe(slot.Name, slot.StoreType, value));
            }

            return parameters;
        }

        /// <summary>Describes one slot's parameter: typed by the slot's store type, sized so no value is clipped.</summary>
        /// <param name="name">The slot's name.</param>
        /// <param name="store">The slot's store type.</param>
        /// <param name="value">The value, or null for SQL <c>NULL</c>.</param>
        /// <returns>The parameter.</returns>
        /// <exception cref="InvalidOperationException">The store type has no bound representation.</exception>
        public static ScalarParameter Describe(string name, QueryexStoreType store, object? value)
        {
            // The provider binds a date as a DateTime.
            object? bound = value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value;
            return store.Family switch
            {
                QueryexStoreFamily.QxBit => new ScalarParameter(name, SqlDbType.Bit, bound),
                QueryexStoreFamily.QxTinyInt => new ScalarParameter(name, SqlDbType.TinyInt, bound),
                QueryexStoreFamily.QxSmallInt => new ScalarParameter(name, SqlDbType.SmallInt, bound),
                QueryexStoreFamily.QxInt => new ScalarParameter(name, SqlDbType.Int, bound),
                QueryexStoreFamily.QxBigInt => new ScalarParameter(name, SqlDbType.BigInt, bound),

                // A written number states its facets; a value the caller supplies leaves them to the
                // provider, which reads them off the value and so never rounds it.
                QueryexStoreFamily.QxDecimal => store.Size is int precision && store.Scale is int scale
                    ? new ScalarParameter(name, SqlDbType.Decimal, bound, precision: (byte)precision, scale: (byte)scale)
                    : new ScalarParameter(name, SqlDbType.Decimal, bound),

                // A length the slot does not state is the bound when the value fits it, so equal
                // statements share a plan, and unbounded when it does not, so nothing is clipped.
                QueryexStoreFamily.QxChar => new ScalarParameter(name, SqlDbType.Char, bound, store.Size ?? Length(bound, NarrowBound)),
                QueryexStoreFamily.QxVarChar => new ScalarParameter(name, SqlDbType.VarChar, bound, store.Size ?? Length(bound, NarrowBound)),
                QueryexStoreFamily.QxNChar => new ScalarParameter(name, SqlDbType.NChar, bound, store.Size ?? Length(bound, UnicodeBound)),
                QueryexStoreFamily.QxNVarChar => new ScalarParameter(name, SqlDbType.NVarChar, bound, store.Size ?? Length(bound, UnicodeBound)),

                QueryexStoreFamily.QxUniqueIdentifier => new ScalarParameter(name, SqlDbType.UniqueIdentifier, bound),
                QueryexStoreFamily.QxDate => new ScalarParameter(name, SqlDbType.Date, bound),
                QueryexStoreFamily.QxDateTime => new ScalarParameter(name, SqlDbType.DateTime, bound),
                QueryexStoreFamily.QxDateTime2 => new ScalarParameter(name, SqlDbType.DateTime2, bound),
                QueryexStoreFamily.QxDateTimeOffset => new ScalarParameter(name, SqlDbType.DateTimeOffset, bound),
                QueryexStoreFamily.QxHierarchyId or QueryexStoreFamily.QxGeography => throw new InvalidOperationException(
                    $"The slot {name} is of the store family {store.Family}, which has no bound representation."),
                _ => throw new InvalidOperationException($"The slot {name} is of an unknown store family, {store.Family}."),
            };
        }

        /// <summary>The declared length of a text parameter whose slot states none.</summary>
        /// <param name="value">The value.</param>
        /// <param name="bound">The widest bounded length of the family.</param>
        /// <returns>The bound, or -1 (unbounded) for a longer value.</returns>
        private static int Length(object? value, int bound)
        {
            return value is string text && text.Length > bound ? -1 : bound;
        }
    }
}
