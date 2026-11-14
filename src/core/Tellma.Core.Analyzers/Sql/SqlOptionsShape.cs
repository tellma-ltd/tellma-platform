// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Tellma.Core.Analyzers.Entities;

namespace Tellma.Core.Analyzers.Sql
{
    /// <summary>What the code shows of a raw statement's declared writes.</summary>
    internal enum WritesShape
    {
        /// <summary>No writes: no options, <c>SqlOptions.None</c>, or options built without a non-empty set.</summary>
        Absent,

        /// <summary>A set the code does not spell out, such as a field or a helper's result.</summary>
        Opaque,

        /// <summary>A set spelled out at the call, every entry a <c>new TableName(...)</c> of constants.</summary>
        Listed,
    }

    /// <summary>
    ///     The <c>SqlOptions</c> argument of an <c>IDataBatch.Sql</c> call as far as the code at the
    ///     call shows it: whether it declares writes, which, and whether it names the affected users.
    /// </summary>
    internal sealed class SqlOptionsShape
    {
        /// <summary>Creates a shape.</summary>
        /// <param name="writes">What the writes are.</param>
        /// <param name="listed">The listed tables, for <see cref="WritesShape.Listed" />.</param>
        /// <param name="namesUsers">Whether the options name the affected users, or might.</param>
        private SqlOptionsShape(WritesShape writes, IReadOnlyList<TableKey> listed, bool namesUsers)
        {
            Writes = writes;
            Listed = listed;
            NamesUsers = namesUsers;
        }

        /// <summary>What the writes are.</summary>
        public WritesShape Writes { get; }

        /// <summary>The listed tables, empty unless <see cref="Writes" /> is <see cref="WritesShape.Listed" />.</summary>
        public IReadOnlyList<TableKey> Listed { get; }

        /// <summary>Whether the options name the affected users (<c>UserIds</c> or <c>ForCaller</c>), or the code does not show.</summary>
        public bool NamesUsers { get; }

        /// <summary>Reads the shape of an options argument.</summary>
        /// <param name="options">The argument's value, or null when the call omitted it.</param>
        /// <returns>The shape.</returns>
        public static SqlOptionsShape From(IOperation? options)
        {
            switch (options?.WithoutConversions())
            {
                // --- No options at all ---
                case null:
                case ILiteralOperation { ConstantValue: { HasValue: true, Value: null } }:
                case IDefaultValueOperation:
                    return new SqlOptionsShape(WritesShape.Absent, [], namesUsers: false);

                // --- SqlOptions.None ---
                case IPropertyReferenceOperation { Property.Name: "None" } none when none.Property.ContainingType.Is(KnownNames.DataNamespace, KnownNames.SqlOptions):
                    return new SqlOptionsShape(WritesShape.Absent, [], namesUsers: false);

                // --- new SqlOptions(writes: ..., userIds: ...) ---
                case IObjectCreationOperation creation when creation.Type.Is(KnownNames.DataNamespace, KnownNames.SqlOptions):
                    (WritesShape writes, IReadOnlyList<TableKey> listed) = ReadWrites(creation.Arguments.ArgumentFor("writes"));
                    IOperation? userIds = creation.Arguments.ArgumentFor("userIds");
                    bool namesUsers = userIds is not null && !IsNull(userIds);
                    return new SqlOptionsShape(writes, listed, namesUsers);

                // --- SqlOptions.ForCaller(writes) ---
                case IInvocationOperation { TargetMethod.Name: "ForCaller" } forCaller when forCaller.TargetMethod.ContainingType.Is(KnownNames.DataNamespace, KnownNames.SqlOptions):
                    (WritesShape callerWrites, IReadOnlyList<TableKey> callerListed) = ReadWrites(forCaller.Arguments.ArgumentFor("writes"));
                    return new SqlOptionsShape(callerWrites, callerListed, namesUsers: true);

                // --- Anything else: a variable, a field, a with-expression ---
                default:
                    return new SqlOptionsShape(WritesShape.Opaque, [], namesUsers: true);
            }
        }

        /// <summary>Whether a value is the null literal or <c>default</c>.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsNull(IOperation value)
        {
            return value.WithoutConversions() is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } or IDefaultValueOperation;
        }

        /// <summary>Reads a written-tables argument.</summary>
        /// <param name="writes">The argument's value, or null when omitted.</param>
        /// <returns>The shape, and the tables when listed.</returns>
        private static (WritesShape Writes, IReadOnlyList<TableKey> Listed) ReadWrites(IOperation? writes)
        {
            if (writes is null || IsNull(writes))
            {
                return (WritesShape.Absent, []);
            }

            // --- A collection expression ([new("core", "Jobs")]) or a set with an initializer ---
            IEnumerable<IOperation>? elements = writes.WithoutConversions() switch
            {
                ICollectionExpressionOperation collection => collection.Elements,
                IObjectCreationOperation { Initializer: IObjectOrCollectionInitializerOperation initializer } when IsEmptyCreation(writes) =>
                    initializer.Initializers.Select(static element => element is IInvocationOperation { Arguments.Length: 1 } add ? add.Arguments[0].Value : element),
                IObjectCreationOperation { Initializer: null } when IsEmptyCreation(writes) => [],
                _ => null,
            };
            if (elements is null)
            {
                return (WritesShape.Opaque, []);
            }

            List<TableKey> listed = [];
            foreach (IOperation element in elements)
            {
                if (TableOf(element) is not TableKey table)
                {
                    return (WritesShape.Opaque, []);
                }

                listed.Add(table);
            }

            return listed.Count == 0 ? (WritesShape.Absent, []) : (WritesShape.Listed, listed);
        }

        /// <summary>Whether a set creation passes no constructor arguments, so its initializer is all it holds.</summary>
        /// <param name="writes">The creation.</param>
        /// <returns>True when it is.</returns>
        private static bool IsEmptyCreation(IOperation writes)
        {
            return writes.WithoutConversions() is IObjectCreationOperation creation && creation.Arguments.All(static argument => argument.ArgumentKind == ArgumentKind.DefaultValue);
        }

        /// <summary>Reads a <c>new TableName(schema, name)</c> of constants.</summary>
        /// <param name="element">The element.</param>
        /// <returns>The table, or null when it is anything else.</returns>
        private static TableKey? TableOf(IOperation element)
        {
            return element.WithoutConversions() is IObjectCreationOperation { Arguments.Length: 2 } creation
                && creation.Type.Is(KnownNames.DataNamespace, KnownNames.TableName)
                && creation.Arguments.ArgumentFor("Schema")?.ConstantValue is { HasValue: true, Value: string schema }
                && creation.Arguments.ArgumentFor("Name")?.ConstantValue is { HasValue: true, Value: string name }
                ? new TableKey(schema, name)
                : null;
        }
    }
}
