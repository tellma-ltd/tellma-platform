// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>The leaves of a plan, and the tables they read from.</summary>
    internal sealed partial class Lowerer
    {
        /// <summary>What makes two parameter slots the same slot.</summary>
        /// <param name="Type">The value's type in the language.</param>
        /// <param name="StoreType">The type it binds as.</param>
        /// <param name="Origin">Where the value comes from.</param>
        /// <param name="Value">The value, when it is fixed at compile time.</param>
        /// <param name="DeclaredName">The declared parameter's name, when it has one.</param>
        private readonly record struct SlotKey(
            BoundType Type,
            QueryexStoreType StoreType,
            QueryexParameterOrigin Origin,
            object? Value,
            string? DeclaredName);

        /// <summary>What makes two hoisted lookups the same lookup.</summary>
        /// <param name="Entity">The entity a node is looked up in.</param>
        /// <param name="LookupProperty">The property a key is matched against.</param>
        /// <param name="NodeProperty">The hierarchy-node property that is read.</param>
        /// <param name="Slot">
        ///     The slot the key came from. Null where the key is not a single bound value, in which
        ///     case no two lookups are treated as the same and each gets its own variable.
        /// </param>
        private readonly record struct VariableKey(
            EntityDescriptor Entity,
            PropertyDescriptor LookupProperty,
            PropertyDescriptor NodeProperty,
            ParameterSlot? Slot);

        /// <summary>Lowers a literal.</summary>
        /// <param name="literal">The bound literal.</param>
        /// <returns>The lowered value.</returns>
        private PlanValue LowerLiteral(TypedLiteral literal)
        {
            if (literal.Value is bool flag)
            {
                // The two truth values are written as the backend's own, which is engine-authored
                // text rather than anything derived from what a user wrote.
                return new PlanBoolConstant(literal.Span, flag);
            }

            QueryexStoreType store = StoreTypes.Adopted(
                literal.Type,
                literal.Value,
                literal.Precision,
                literal.Scale,
                _hint);

            return new PlanParameterRef(
                literal.Span,
                Slot(literal.Type, store, QueryexParameterOrigin.Literal, literal.Value, null, literal.Span));
        }

        /// <summary>Lowers a declared parameter.</summary>
        /// <param name="parameter">The bound parameter.</param>
        /// <returns>The lowered value.</returns>
        /// <remarks>
        ///     One declared parameter compared against columns of two different families becomes two
        ///     slots carrying the same declared name, which the host binds from the one value it was
        ///     given.
        /// </remarks>
        private PlanParameterRef LowerParameter(TypedParameter parameter)
        {
            QueryexStoreType store = StoreTypes.Adopted(parameter.Symbol.Type, null, 0, 0, _hint);
            return new PlanParameterRef(
                parameter.Span,
                Slot(
                    parameter.Symbol.Type,
                    store,
                    QueryexParameterOrigin.Declared,
                    null,
                    parameter.Symbol.Name,
                    parameter.Span));
        }

        /// <summary>Lowers a path to the column of a joined table.</summary>
        /// <param name="path">The bound path.</param>
        /// <returns>The lowered value.</returns>
        private PlanColumn LowerPath(TypedPath path)
        {
            JoinNode table = Walk(path.Navigations, path.Span);
            return new PlanColumn(path.Span, table, path.Property);
        }

        /// <summary>Follows a run of navigations from the root, creating joins as needed.</summary>
        /// <param name="navigations">The steps.</param>
        /// <param name="span">The range of the path that needed them.</param>
        /// <returns>The node the last step leads to.</returns>
        private JoinNode Walk(ImmutableArray<NavigationDescriptor> navigations, QueryexSpan span)
        {
            JoinNode table = Root;
            PlanOrigin origin = new(span, _location);
            foreach (NavigationDescriptor navigation in navigations)
            {
                table = table.Step(navigation, origin);
            }

            return table;
        }

        /// <summary>Finds or allocates the slot a value binds through.</summary>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="store">The type it binds as.</param>
        /// <param name="origin">Where the value comes from.</param>
        /// <param name="value">The value, when it is fixed at compile time.</param>
        /// <param name="declaredName">The declared parameter's name, when it has one.</param>
        /// <param name="span">The range of the node that needed it.</param>
        /// <returns>The slot.</returns>
        private ParameterSlot Slot(
            BoundType type,
            QueryexStoreType store,
            QueryexParameterOrigin origin,
            object? value,
            string? declaredName,
            QueryexSpan span)
        {
            SlotKey key = new(type, store, origin, value, declaredName);
            if (_slotIndex.TryGetValue(key, out ParameterSlot? existing))
            {
                return existing;
            }

            ParameterSlot slot = new(type, store, origin, value, declaredName, new PlanOrigin(span, _location));
            _slotIndex.Add(key, slot);
            _slots.Add(slot);
            return slot;
        }

        /// <summary>Finds or allocates the variable one hierarchy lookup is computed into.</summary>
        /// <param name="entity">The entity a node is looked up in.</param>
        /// <param name="lookup">The property the key is matched against.</param>
        /// <param name="node">The hierarchy-node property that is read.</param>
        /// <param name="key">The key to match.</param>
        /// <returns>The variable.</returns>
        private HoistedVariable Hoist(
            EntityDescriptor entity,
            PropertyDescriptor lookup,
            PropertyDescriptor node,
            PlanValue key)
        {
            ParameterSlot? slot = (key as PlanParameterRef)?.Slot;
            VariableKey identity = new(entity, lookup, node, slot);
            if (slot is not null && _variableIndex.TryGetValue(identity, out HoistedVariable? existing))
            {
                return existing;
            }

            HoistedVariable variable = new(
                _variables.Count,
                BoundType.HierarchyId,
                entity,
                lookup,
                node,
                key);

            _variables.Add(variable);
            if (slot is not null)
            {
                _variableIndex.Add(identity, variable);
            }

            return variable;
        }

        /// <summary>
        ///     Makes a value safe to write more than once, giving it a name if it needs one.
        /// </summary>
        /// <param name="value">The value, replaced by a reference to its binding when one is made.</param>
        /// <returns>True when the value may now be written as many times as needed.</returns>
        private bool TryShare(ref PlanValue value)
        {
            if (value.IsAtomic)
            {
                return true;
            }

            if (!_bindingsAllowed)
            {
                return false;
            }

            ValueBinding binding = new(_bindings.Count, value, value.Type);
            _bindings.Add(binding);
            value = new PlanBindingRef(value.Span, binding);
            return true;
        }

        /// <summary>The column a value is being compared against, when it is a bare path.</summary>
        /// <param name="node">The other operand.</param>
        /// <returns>That column's type, or null when the operand names no column.</returns>
        private static QueryexStoreType? HintOf(TypedExpr node)
        {
            return node is TypedPath path ? StoreTypes.OfProperty(path.Property) : null;
        }

        /// <summary>Lowers a value knowing which column it will be compared against.</summary>
        /// <param name="node">The bound node.</param>
        /// <param name="hint">That column's type, or null when there is none.</param>
        /// <returns>The lowered value.</returns>
        private PlanValue LowerValueAgainst(TypedExpr node, QueryexStoreType? hint)
        {
            QueryexStoreType? saved = _hint;
            _hint = hint;
            PlanValue value = LowerValue(node);
            _hint = saved;
            return value;
        }
    }
}
