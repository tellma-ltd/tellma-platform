// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     The errors one operation's validation collects. Validators add to it and carry on; when the
    ///     validation outcome is known, any error fails the operation with one
    ///     <see cref="ValidationException" /> carrying them all.
    /// </summary>
    /// <remarks>
    ///     Safe for concurrent use: the validators of one round run concurrently and add to the same
    ///     instance. <see cref="Items" /> is a snapshot in the order the errors were added.
    /// </remarks>
    public class ValidationErrors
    {
        private readonly Lock _gate = new();
        private readonly List<ValidationError> _items = [];
        private ReadOnlyCollection<ValidationError>? _snapshot;

        /// <summary>Whether any error was added.</summary>
        public bool HasErrors
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count > 0;
                }
            }
        }

        /// <summary>The errors added so far, in the order they were added.</summary>
        public IReadOnlyList<ValidationError> Items
        {
            get
            {
                lock (_gate)
                {
                    // One snapshot serves every read until the next Add, so reading the list in a
                    // loop costs nothing and never observes a half-written state.
                    return _snapshot ??= Array.AsReadOnly([.. _items]);
                }
            }
        }

        /// <summary>
        ///     Adds an error at a path. Use this overload for a path an expression cannot name: a
        ///     root-level name (<c>Ids[2]</c>), a child row, the whole payload.
        /// </summary>
        /// <param name="path">The value the error concerns.</param>
        /// <param name="code">The error's code, a dotted PascalCase resource key.</param>
        /// <param name="arguments">The raw values its message renders from; none when null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="path" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="code" /> is null, empty or white space.</exception>
        public void Add(ValidationPath path, string code, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            ValidationError error = new(path, code, arguments ?? ReadOnlyDictionary<string, object?>.Empty);

            lock (_gate)
            {
                _items.Add(error);
                _snapshot = null;
            }
        }
    }

    /// <summary>
    ///     The typed view of <see cref="ValidationErrors" /> every validation context exposes: an
    ///     error on a payload row's property names the property with an expression, so the member
    ///     name is checked by the compiler rather than spelled in a string.
    /// </summary>
    /// <typeparam name="TEntity">The payload's entity type.</typeparam>
    /// <example>
    ///     <code>ctx.Errors.Add(i, x => x.ParentId, GlValidationCodes.ParentMustBeGrouping);</code>
    /// </example>
    public sealed class ValidationErrors<TEntity> : ValidationErrors
    {
        /// <summary>
        ///     Adds an error at a property of a payload row: the path
        ///     <c>Root.Index(index).Property(name)</c>, <c>[i].ParentId</c> in bracket form.
        /// </summary>
        /// <typeparam name="TValue">The property's type.</typeparam>
        /// <param name="index">The payload row, zero-based.</param>
        /// <param name="property">
        ///     The property, read directly off the row: <c>x =&gt; x.ParentId</c>, or through a cast
        ///     to an interface the entity implements. A deeper or computed expression is refused.
        /// </param>
        /// <param name="code">The error's code, a dotted PascalCase resource key.</param>
        /// <param name="arguments">The raw values its message renders from; none when null.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="property" /> does not read a property of the row, or <paramref name="code" /> is empty.</exception>
        public void Add<TValue>(
            int index, Expression<Func<TEntity, TValue>> property, string code, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            ArgumentNullException.ThrowIfNull(property);
            Add(ValidationPath.Root.Index(index).Property(PropertyName(property)), code, arguments);
        }

        /// <summary>The name of the property an expression reads directly off its parameter.</summary>
        /// <param name="property">The expression.</param>
        /// <returns>The property's CLR name.</returns>
        private static string PropertyName(LambdaExpression property)
        {
            // A value-typed property read into an object-typed TValue arrives boxed under a Convert.
            Expression? body = StripConversions(property.Body);

            // The member must be a property read off the parameter itself (or the parameter cast to
            // an interface): x => x.Parent.Name names no property of the row.
            return body is MemberExpression { Member: PropertyInfo member } access
                && StripConversions(access.Expression) == property.Parameters[0]
                ? member.Name
                : throw new ArgumentException(
                    $"A validation error's property expression reads one property directly off the entity, such as 'x => x.Name'; received '{property}'.",
                    nameof(property));
        }

        /// <summary>Removes the conversions and casts that wrap an expression.</summary>
        /// <param name="expression">The expression.</param>
        /// <returns>The expression inside every conversion.</returns>
        private static Expression? StripConversions(Expression? expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion)
            {
                expression = conversion.Operand;
            }

            return expression;
        }
    }
}
