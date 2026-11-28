// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     Reflection over the platform's entity contract: which base a class derives from, its key
    ///     type, its child collections, and the annotations on its members. The model's conventions and
    ///     its definition read entity classes only through here, so each rule has one reading.
    /// </summary>
    internal static class EntityShapes
    {
        /// <summary>The key type of a keyed entity (<see cref="Entity{TKey}" />), or <see langword="null" /> for any other class.</summary>
        /// <param name="type">The class.</param>
        /// <returns>The key type, or <see langword="null" />.</returns>
        public static Type? KeyTypeOf(Type type)
        {
            return GenericBase(type, typeof(Entity<>))?.GetGenericArguments()[0];
        }

        /// <summary>Whether a class derives from <see cref="Entity{TKey}" />.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it does.</returns>
        public static bool IsKeyed(Type type)
        {
            return KeyTypeOf(type) is not null;
        }

        /// <summary>Whether a class derives from <see cref="TopLevelEntity{TKey}" />.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it does.</returns>
        public static bool IsTopLevel(Type type)
        {
            return GenericBase(type, typeof(TopLevelEntity<>)) is not null;
        }

        /// <summary>Whether a class derives from <see cref="ChildEntity{TKey}" />.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it does.</returns>
        public static bool IsChild(Type type)
        {
            return GenericBase(type, typeof(ChildEntity<>)) is not null;
        }

        /// <summary>Whether a class derives from <see cref="TreeEntity{TKey}" />.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it does.</returns>
        public static bool IsTree(Type type)
        {
            return GenericBase(type, typeof(TreeEntity<>)) is not null;
        }

        /// <summary>Whether a class derives from <see cref="ActivatableTreeEntity{TKey}" />.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it does.</returns>
        public static bool IsActivatableTree(Type type)
        {
            return GenericBase(type, typeof(ActivatableTreeEntity<>)) is not null;
        }

        /// <summary>
        ///     The child collections a class declares: every public <c>[NotMapped]</c> property a
        ///     <see cref="List{T}" /> of a <see cref="ChildEntity{TKey}" /> can be stored in, in
        ///     declaration order, base classes first.
        /// </summary>
        /// <param name="type">The parent class.</param>
        /// <returns>Each collection property with its child type, as declared (before any leaf substitution).</returns>
        public static IReadOnlyList<(PropertyInfo Property, Type ChildType)> ChildCollections(Type type)
        {
            List<(PropertyInfo, Type)> collections = [];
            foreach (PropertyInfo property in DeclaredProperties(type))
            {
                if (property.GetIndexParameters().Length > 0
                    || property.GetMethod is not { IsStatic: false, IsPublic: true }
                    || GetAttribute<NotMappedAttribute>(property) is null
                    || !property.PropertyType.IsGenericType
                    || property.PropertyType.GetGenericArguments() is not [Type element]
                    || !IsChild(element)
                    || !property.PropertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(element)))
                {
                    continue;
                }

                collections.Add((property, element));
            }

            return collections;
        }

        /// <summary>
        ///     The public instance properties of a class in declaration order, base classes first, each
        ///     once: an override is listed where its base declared it.
        /// </summary>
        /// <param name="type">The class.</param>
        /// <returns>The properties.</returns>
        public static IReadOnlyList<PropertyInfo> DeclaredProperties(Type type)
        {
            List<Type> chain = [];
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                chain.Add(current);
            }

            chain.Reverse();

            List<PropertyInfo> properties = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (Type declaring in chain)
            {
                foreach (PropertyInfo property in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (seen.Add(property.Name))
                    {
                        properties.Add(property);
                    }
                }
            }

            return properties;
        }

        /// <summary>
        ///     An attribute on a property, inherited ones included. Reads through
        ///     <see cref="Attribute.GetCustomAttribute(MemberInfo, Type, bool)" />, which, unlike
        ///     <see cref="MemberInfo.GetCustomAttributes(bool)" />, walks overridden properties.
        /// </summary>
        /// <typeparam name="TAttribute">The attribute type.</typeparam>
        /// <param name="property">The property.</param>
        /// <returns>The attribute, or <see langword="null" />.</returns>
        public static TAttribute? GetAttribute<TAttribute>(PropertyInfo property)
            where TAttribute : Attribute
        {
            return Attribute.GetCustomAttribute(property, typeof(TAttribute), inherit: true) as TAttribute;
        }

        /// <summary>An attribute on a class, inherited ones included.</summary>
        /// <typeparam name="TAttribute">The attribute type.</typeparam>
        /// <param name="type">The class.</param>
        /// <returns>The attribute, or <see langword="null" />.</returns>
        public static TAttribute? GetAttribute<TAttribute>(Type type)
            where TAttribute : Attribute
        {
            return type.GetCustomAttribute<TAttribute>(inherit: true);
        }

        /// <summary>The closed generic base of a class built on <paramref name="openBase" />, or <see langword="null" />.</summary>
        /// <param name="type">The class.</param>
        /// <param name="openBase">The open generic base class definition.</param>
        /// <returns>The closed base, or <see langword="null" />.</returns>
        private static Type? GenericBase(Type type, Type openBase)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openBase)
                {
                    return current;
                }
            }

            return null;
        }
    }
}
