// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.6 step 5: the payload's values of one unique property, loaded in round trip 1 through
    // the loader's grouped load (the rows holding each value), then compared in C# against the payload
    // itself and against what was loaded, scoped by parent for a property unique within its parent.
    /// <summary>
    ///     The uniqueness check of one unique property of one entity: the payload rows that hold a
    ///     value of it, and the stored rows holding the same values.
    /// </summary>
    /// <param name="rules">The rules of the property's entity.</param>
    /// <param name="property">The property.</param>
    /// <param name="parentKey">The parent key that scopes the property, for a property unique only within its parent; null for one unique on its own.</param>
    internal abstract class UniqueProbe(RowRules rules, PropertyMetadata property, PropertyMetadata? parentKey)
    {
        /// <summary>The rules of the property's entity.</summary>
        public RowRules Rules { get; } = rules;

        /// <summary>The property.</summary>
        public PropertyMetadata Property { get; } = property;

        /// <summary>The parent key that scopes the property; null when it is unique on its own.</summary>
        public PropertyMetadata? ParentKey { get; } = parentKey;

        /// <summary>The payload rows holding a value of the property, with the value, in payload order.</summary>
        public List<(SaveRow Row, object Value)> Holders { get; } = [];

        /// <summary>How the property's values compare: as the database's collation compares strings, by equality otherwise.</summary>
        public abstract IEqualityComparer<object> Comparer { get; }

        /// <summary>Creates the check of a property, over the property's type.</summary>
        /// <param name="rules">The rules of the property's entity.</param>
        /// <param name="property">The property, one of the rules' unique properties.</param>
        /// <param name="parentKey">The parent key that scopes it, or null.</param>
        /// <returns>The check.</returns>
        public static UniqueProbe Create(RowRules rules, PropertyMetadata property, PropertyMetadata? parentKey)
        {
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(property);
            Type valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            return (UniqueProbe)Activator.CreateInstance(typeof(UniqueProbe<,>).MakeGenericType(rules.Entity.ClrType, valueType), rules, property, parentKey)!;
        }

        /// <summary>Requests the stored rows holding the payload's values from the loader.</summary>
        /// <param name="loader">The operation's loader.</param>
        public abstract void Request(ContextLoader loader);

        /// <summary>The stored rows holding a value, once the round ran: each row's id, and its parent's when the property is scoped.</summary>
        /// <param name="value">The value, one the payload holds.</param>
        /// <returns>The rows.</returns>
        public abstract IEnumerable<(long Id, long? ParentId)> Loaded(object value);
    }

    /// <summary>The uniqueness check of one unique property, over the entity class and the property's type.</summary>
    /// <typeparam name="T">The entity class.</typeparam>
    /// <typeparam name="TK">The property's type, nullable stripped.</typeparam>
    /// <param name="rules">The rules of the property's entity.</param>
    /// <param name="property">The property.</param>
    /// <param name="parentKey">The parent key that scopes it, or null.</param>
    internal sealed class UniqueProbe<T, TK>(RowRules rules, PropertyMetadata property, PropertyMetadata? parentKey) : UniqueProbe(rules, property, parentKey)
        where T : class
        where TK : notnull
    {
        /// <summary>The stored rows by value, once requested.</summary>
        private ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<T>>>? _loaded;

        /// <inheritdoc />
        public override IEqualityComparer<object> Comparer { get; } = typeof(TK) == typeof(string) ? CollationObjectComparer.Instance : EqualityComparer<object>.Default;

        /// <inheritdoc />
        public override void Request(ContextLoader loader)
        {
            ArgumentNullException.ThrowIfNull(loader);

            // --- One grouped load over the values, which the loader makes distinct; the key and the parent key ride the select ---
            TK[] values = [.. Holders.Select(static holder => (TK)holder.Value)];
            string select = ParentKey is null
                ? $"{Rules.Entity.Key.Name},{Property.Name}"
                : $"{Rules.Entity.Key.Name},{Property.Name},{ParentKey.Name}";
            _loaded = loader.ByValues<T, TK>(Property, values, select);
        }

        /// <inheritdoc />
        public override IEnumerable<(long Id, long? ParentId)> Loaded(object value)
        {
            ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<T>>> loaded = _loaded
                ?? throw new InvalidOperationException("The uniqueness check reads its rows after requesting them.");
            if (!loaded.Value.TryGetValue((TK)value, out IReadOnlyList<T>? rows))
            {
                yield break;
            }

            foreach (T row in rows)
            {
                long id = Convert.ToInt64(Rules.Entity.Key.Getter(row), CultureInfo.InvariantCulture);
                long? parentId = ParentKey?.Getter(row) is object parent ? Convert.ToInt64(parent, CultureInfo.InvariantCulture) : null;
                yield return (id, parentId);
            }
        }
    }

    /// <summary>Boxed strings compared as the database's collation compares them; anything else by equality.</summary>
    internal sealed class CollationObjectComparer : IEqualityComparer<object>
    {
        /// <summary>The one instance.</summary>
        public static CollationObjectComparer Instance { get; } = new();

        /// <inheritdoc />
        public new bool Equals(object? x, object? y)
        {
            return x is string left && y is string right ? ContextLoader.CollationKeyComparer.Instance.Equals(left, right) : object.Equals(x, y);
        }

        /// <inheritdoc />
        public int GetHashCode(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return obj is string text ? ContextLoader.CollationKeyComparer.Instance.GetHashCode(text) : obj.GetHashCode();
        }
    }
}
