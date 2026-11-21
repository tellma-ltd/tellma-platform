// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.Reflection;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Sizes every enum stored by name as <c>varchar(n)</c>: non-unicode, with
    ///     <c>n = max(8, the longest value)</c>, and no <c>CHECK</c> constraint listing the members.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>[MaxLength]</c>, a <c>[Unicode]</c> or a configured facet wins over the convention's
    ///         value for that facet, and an explicit column type skips the convention altogether.
    ///     </para>
    ///     <para>
    ///         The longest value of a <c>[Flags]</c> enum is every named flag written together
    ///         (<c>"Read, Write"</c>), which is how the string conversion stores a combination; the
    ///         longest value of any other enum is its longest member name.
    ///     </para>
    /// </remarks>
    internal sealed class EnumStringLengthConvention : IModelFinalizingConvention
    {
        /// <summary>The shortest an enum column is ever sized, so a short enum can grow a member without a migration.</summary>
        public const int MinimumLength = 8;

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    Type? enumType = EnumStringConvention.EnumType(property);
                    if (enumType is null
                        || property.GetProviderClrType() != typeof(string)
                        || IsConfigured(property.GetColumnTypeConfigurationSource()))
                    {
                        continue;
                    }

                    if (!IsConfigured(property.GetMaxLengthConfigurationSource()))
                    {
                        _ = property.Builder.HasMaxLength(LengthOf(enumType), fromDataAnnotation: true);
                    }

                    if (!IsConfigured(property.GetIsUnicodeConfigurationSource()))
                    {
                        _ = property.Builder.IsUnicode(false, fromDataAnnotation: true);
                    }
                }
            }
        }

        /// <summary>The column length of an enum stored by name.</summary>
        /// <param name="enumType">The enum type.</param>
        /// <returns><c>max(8, the longest value)</c>.</returns>
        public static int LengthOf(Type enumType)
        {
            string[] names = Enum.GetNames(enumType);
            int longest = names.Length == 0 ? 0 : names.Max(static name => name.Length);

            // A flags enum stores a combination as its flag names joined by ", ", and the widest
            // combination joins every flag. Members that are zero or combinations of other flags
            // add nothing a single-bit member does not already contribute.
            if (enumType.GetCustomAttribute<FlagsAttribute>() is not null)
            {
                // GetNames and GetValuesAsUnderlyingType list the members in the same order.
                Array values = Enum.GetValuesAsUnderlyingType(enumType);
                List<string> flags = [];
                for (int i = 0; i < names.Length; i++)
                {
                    if (IsSingleBit(Bits(values.GetValue(i)!)))
                    {
                        flags.Add(names[i]);
                    }
                }

                int combined = flags.Count == 0 ? 0 : flags.Sum(static name => name.Length) + (2 * (flags.Count - 1));
                longest = Math.Max(longest, combined);
            }

            return Math.Max(MinimumLength, longest);
        }

        /// <summary>The bit pattern of an enum member's underlying value, at the underlying type's width.</summary>
        /// <param name="value">The underlying value.</param>
        /// <returns>The bits, zero-extended.</returns>
        private static ulong Bits(object value)
        {
            return value switch
            {
                byte v => v,
                sbyte v => unchecked((byte)v),
                short v => unchecked((ushort)v),
                ushort v => v,
                int v => unchecked((uint)v),
                uint v => v,
                long v => unchecked((ulong)v),
                ulong v => v,
                _ => 0,
            };
        }

        /// <summary>Whether a facet was set by a declaration or a model configuration, rather than left to conventions.</summary>
        /// <param name="source">The facet's configuration source.</param>
        /// <returns>True for a data annotation or an explicit configuration.</returns>
        private static bool IsConfigured(ConfigurationSource? source)
        {
            return source is ConfigurationSource.DataAnnotation or ConfigurationSource.Explicit;
        }

        /// <summary>Whether a value has exactly one bit set.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True for a single bit.</returns>
        private static bool IsSingleBit(ulong value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }
}
