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
        /// <summary>The argument a re-homed error names the stack it was raised for under: the enlisted target's entity name.</summary>
        internal const string EnlistedResourceArgument = "enlistedResource";

        private readonly Lock _gate = new();
        private readonly List<ValidationError> _items = [];
        private ReadOnlyCollection<ValidationError>? _snapshot;

        /// <summary>The list an enlisted stack's errors land in, re-homed; null for an operation's own list.</summary>
        private readonly ValidationErrors? _host;

        /// <summary>Maps a path of the enlisted stack's payload onto its host's; null maps every path to the host's root.</summary>
        private readonly Func<ValidationPath, ValidationPath>? _map;

        /// <summary>The enlisted stack's entity name, which every re-homed error carries; null for an operation's own list.</summary>
        private readonly string? _enlistedResource;

        /// <summary>Creates an operation's own list of errors.</summary>
        public ValidationErrors()
        {
        }

        // Spec 0015 §13.3: each participant of an enlisted group writes its own paths, and the view
        // re-homes every error into the host's one list through the enlistment's path map, applied to
        // the whole path, adding the target's entity name. A view over a view composes the maps along
        // the nesting, the innermost first, and keeps the innermost target's name.
        /// <summary>Creates the re-homing view an enlisted stack's participants write their errors to.</summary>
        /// <param name="host">The host's list, or the view of the group the stack was enlisted from.</param>
        /// <param name="map">Maps a path of the enlisted stack's payload onto the host's; null maps every path to the host's root.</param>
        /// <param name="enlistedResource">The enlisted stack's entity name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="host" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="enlistedResource" /> is null, empty or white space.</exception>
        internal ValidationErrors(ValidationErrors host, Func<ValidationPath, ValidationPath>? map, string enlistedResource)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentException.ThrowIfNullOrWhiteSpace(enlistedResource);
            _host = host;
            _map = map;
            _enlistedResource = enlistedResource;
        }

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

            // --- The re-homed error first, so a path map that refuses leaves this list untouched ---
            ValidationError? rehomed = _host is null ? null : Rehome(error);
            lock (_gate)
            {
                _items.Add(error);
                _snapshot = null;
            }

            _host?.AddRehomed(rehomed!);
        }

        /// <summary>
        ///     The error as the operation's own list holds it: re-homed through every enlistment between
        ///     this list and the operation's, the innermost first; unchanged for an operation's own list.
        /// </summary>
        /// <param name="error">An error at a path of this list's payload.</param>
        /// <returns>The error at the operation's path, naming the innermost enlisted stack.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error" /> is null.</exception>
        /// <exception cref="InvalidOperationException">An enlistment's path map returned null.</exception>
        internal ValidationError Rehome(ValidationError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            if (_host is null)
            {
                return error;
            }

            // --- This enlistment's map over the whole path; the default lands on the host's root ---
            ValidationPath path = _map is null
                ? ValidationPath.Root
                : _map(error.Path) ?? throw new InvalidOperationException(
                    $"The path map of the enlisted write to {_enlistedResource} returned null for the path '{error.Path}'. Map every path onto one of the host's, ValidationPath.Root when no other fits.");

            // --- The innermost enlisted stack's name, kept as the error moves outward ---
            IReadOnlyDictionary<string, object?> arguments = error.Arguments;
            if (!arguments.ContainsKey(EnlistedResourceArgument))
            {
                Dictionary<string, object?> named = new(arguments, StringComparer.Ordinal) { [EnlistedResourceArgument] = _enlistedResource };
                arguments = named.AsReadOnly();
            }

            return _host.Rehome(new ValidationError(path, error.Code, arguments));
        }

        /// <summary>Adds an error another list already re-homed onto the operation's paths: only the operation's own list holds it.</summary>
        /// <param name="error">The re-homed error.</param>
        private void AddRehomed(ValidationError error)
        {
            if (_host is not null)
            {
                _host.AddRehomed(error);
                return;
            }

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
        /// <summary>Creates an operation's own list of errors.</summary>
        public ValidationErrors()
        {
        }

        /// <summary>Creates the re-homing view an enlisted stack's participants write their errors to.</summary>
        /// <param name="host">The host's list, or the view of the group the stack was enlisted from.</param>
        /// <param name="map">Maps a path of the enlisted stack's payload onto the host's; null maps every path to the host's root.</param>
        /// <param name="enlistedResource">The enlisted stack's entity name.</param>
        internal ValidationErrors(ValidationErrors host, Func<ValidationPath, ValidationPath>? map, string enlistedResource)
            : base(host, map, enlistedResource)
        {
        }

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
