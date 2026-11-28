// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     Compiles the accessors entity metadata carries: a property's getter and setter over
    ///     <see cref="object" />, a child collection's getter and an entity's parameterless constructor.
    ///     Each is compiled once at startup, so reading and writing rows costs a delegate call, not
    ///     reflection.
    /// </summary>
    internal static class PropertyAccessors
    {
        /// <summary><see cref="InvalidOperationException(string)" />, which a setter throws when given null for a non-nullable value type.</summary>
        private static readonly ConstructorInfo InvalidOperationConstructor =
            typeof(InvalidOperationException).GetConstructor([typeof(string)])!;

        /// <summary>Compiles <c>(object entity) =&gt; (object?)((TEntity)entity).Property</c>.</summary>
        /// <param name="entityType">The entity class.</param>
        /// <param name="property">The property, which has a getter.</param>
        /// <returns>The getter.</returns>
        public static Func<object, object?> Getter(Type entityType, PropertyInfo property)
        {
            ParameterExpression entity = Expression.Parameter(typeof(object), "entity");
            Expression member = Expression.Property(Expression.Convert(entity, entityType), property);
            return Expression.Lambda<Func<object, object?>>(Expression.Convert(member, typeof(object)), entity).Compile();
        }

        /// <summary>
        ///     Compiles <c>(object entity, object? value) =&gt; ((TEntity)entity).Property = (TProperty)value</c>,
        ///     through the property's setter of any accessibility, else through the backing field the model
        ///     reads and writes it by. A null value for a non-nullable value type throws an
        ///     <see cref="InvalidOperationException" /> naming the property, rather than an unexplained
        ///     <see cref="NullReferenceException" />.
        /// </summary>
        /// <param name="entityType">The entity class.</param>
        /// <param name="property">The property.</param>
        /// <param name="backingField">The field the model maps the property through, or <see langword="null" />.</param>
        /// <returns>The setter, or <see langword="null" /> when the property has neither a setter nor a backing field.</returns>
        public static Action<object, object?>? Setter(Type entityType, PropertyInfo property, FieldInfo? backingField)
        {
            ParameterExpression entity = Expression.Parameter(typeof(object), "entity");
            ParameterExpression value = Expression.Parameter(typeof(object), "value");
            Expression target = Expression.Convert(entity, entityType);

            // --- The member to assign: the setter when there is one, else the backing field ---
            MemberExpression? member = property.SetMethod is not null
                ? Expression.Property(target, property)
                : backingField is not null ? Expression.Field(target, backingField) : null;
            if (member is null)
            {
                return null;
            }

            // --- The value, converted; null refused for a value type that cannot hold it ---
            Type propertyType = property.PropertyType;
            Expression converted = Expression.Convert(value, propertyType);
            if (propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) is null)
            {
                string message = $"The property {entityType.Name}.{property.Name} is a non-nullable {propertyType.Name} and cannot be set to null.";
                converted = Expression.Condition(
                    Expression.ReferenceEqual(value, Expression.Constant(null)),
                    Expression.Throw(Expression.New(InvalidOperationConstructor, Expression.Constant(message)), propertyType),
                    converted);
            }

            return Expression.Lambda<Action<object, object?>>(Expression.Assign(member, converted), entity, value).Compile();
        }

        /// <summary>
        ///     The getter of a child collection: the list itself when it is already a read-only list of
        ///     objects (every <see cref="List{T}" /> of a class is), else a copy of its items; null stays null.
        /// </summary>
        /// <param name="entityType">The parent class.</param>
        /// <param name="property">The collection property.</param>
        /// <returns>The getter.</returns>
        public static Func<object, IReadOnlyList<object>?> CollectionGetter(Type entityType, PropertyInfo property)
        {
            Func<object, object?> getter = Getter(entityType, property);
            return parent => getter(parent) switch
            {
                null => null,
                IReadOnlyList<object> list => list,
                IEnumerable items => [.. items.Cast<object>()],
                _ => throw new InvalidOperationException($"The collection {entityType.Name}.{property.Name} holds a value that is not a list."),
            };
        }

        /// <summary>Compiles <c>() =&gt; (object)new TEntity()</c> over the class's parameterless constructor, of any accessibility.</summary>
        /// <param name="entityType">The entity class.</param>
        /// <returns>The factory, or <see langword="null" /> when the class has no parameterless constructor or is abstract.</returns>
        public static Func<object>? Factory(Type entityType)
        {
            ConstructorInfo? constructor = entityType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
            return entityType.IsAbstract || constructor is null
                ? null
                : Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor), typeof(object))).Compile();
        }
    }
}
