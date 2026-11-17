// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition;

namespace Tellma.Core.Settings
{
    /// <summary>
    ///     A setting key as its declaration reads: the key, the field that holds it, the feature that
    ///     contributed the field's type, and what its type admits.
    /// </summary>
    /// <param name="Key">The key; null when the field holds none.</param>
    /// <param name="Field">The public static field.</param>
    /// <param name="Owner">The feature that contributed the declaring type.</param>
    /// <param name="IsNullable">Whether the key's type admits null: a nullable value type, or a reference type annotated nullable.</param>
    internal sealed record DeclaredSettingKey(SettingKey? Key, FieldInfo Field, ITellmaFeature Owner, bool IsNullable)
    {
        /// <summary>The field as a message names it: the declaring type's name, a dot, and the field's.</summary>
        public string Describe => $"{Field.DeclaringType?.Name}.{Field.Name}";

        /// <summary>
        ///     Reads the setting-key fields of each contributed type: every public static field whose
        ///     type is <see cref="SettingKey{T}" />, types in contribution order and, within a type,
        ///     declaration order.
        /// </summary>
        /// <param name="types">The contributed declaring types.</param>
        /// <returns>The declarations, a field that holds no key included.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="types" /> is null.</exception>
        public static ImmutableArray<DeclaredSettingKey> Discover(IEnumerable<ContributedType> types)
        {
            ArgumentNullException.ThrowIfNull(types);
            ImmutableArray<DeclaredSettingKey>.Builder declared = ImmutableArray.CreateBuilder<DeclaredSettingKey>();
            NullabilityInfoContext nullability = new();
            foreach (ContributedType contributed in types)
            {
                // Reflection lists fields in no guaranteed order; the metadata token is the declaration order.
                IEnumerable<FieldInfo> fields = contributed.Type
                    .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(static field => IsSettingKeyType(field.FieldType))
                    .OrderBy(static field => field.MetadataToken);
                foreach (FieldInfo field in fields)
                {
                    declared.Add(new DeclaredSettingKey(
                        field.GetValue(null) as SettingKey,
                        field,
                        contributed.Owner,
                        AdmitsNull(field, nullability)));
                }
            }

            return declared.ToImmutable();
        }

        /// <summary>Whether a type is a closed <see cref="SettingKey{T}" />.</summary>
        /// <param name="type">The field's type.</param>
        /// <returns>True when it is.</returns>
        private static bool IsSettingKeyType(Type type)
        {
            return type.IsGenericType && !type.ContainsGenericParameters && type.GetGenericTypeDefinition() == typeof(SettingKey<>);
        }

        /// <summary>Whether the value type argument of a field's key admits null.</summary>
        /// <param name="field">The field, of type <see cref="SettingKey{T}" />.</param>
        /// <param name="nullability">The reader of nullable annotations.</param>
        /// <returns>True for a nullable value type, and for a reference type annotated nullable.</returns>
        private static bool AdmitsNull(FieldInfo field, NullabilityInfoContext nullability)
        {
            Type valueType = field.FieldType.GetGenericArguments()[0];
            if (valueType.IsValueType)
            {
                return Nullable.GetUnderlyingType(valueType) is not null;
            }

            // A reference type argument carries its annotation on the field's own nullability.
            NullabilityInfo info = nullability.Create(field);
            return info.GenericTypeArguments is [NullabilityInfo argument, ..] && argument.ReadState == NullabilityState.Nullable;
        }
    }
}
