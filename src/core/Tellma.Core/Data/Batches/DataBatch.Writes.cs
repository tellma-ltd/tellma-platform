// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    // What the update and delete statements of spec 0012 §9 share: the target entity, the ids the
    // caller names, the compiled key queries, the not-found check of §9.2 and the appending of a
    // call's statements as one unit.
    /// <content>The parts the update and the delete statements share.</content>
    internal sealed partial class DataBatch
    {
        /// <summary>
        ///     The entity an update or a delete writes, refused on a batch that writes nothing: its
        ///     metadata and its query descriptor.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="operation">The operation, for the message.</param>
        /// <returns>The metadata and the descriptor.</returns>
        /// <exception cref="InvalidOperationException">The batch is a read or a validate batch.</exception>
        private (EntityMetadata Metadata, EntityDescriptor Root) WriteTarget<TEntity>(string operation)
            where TEntity : class
        {
            EnsureComposing();
            if (Purpose is BatchPurpose.Read or BatchPurpose.Validate)
            {
                throw new InvalidOperationException(
                    $"A {Purpose} batch writes nothing; {operation.ToLowerInvariant()} belongs to a {nameof(BatchPurpose.Persist)} batch.");
            }

            // The prologue first, so the statements compiled next know the ordinals they take.
            EnsurePrologue();
            EntityMetadata metadata = _executor.Queries.Metadata.Get(typeof(TEntity));
            return (metadata, Map.Descriptor(metadata));
        }

        /// <summary>The ids a caller names, as keys of the entity's type: integral, distinct, and in range.</summary>
        /// <param name="ids">The ids.</param>
        /// <param name="isLong">Whether the key is a <see cref="long" />.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The keys, in order.</returns>
        /// <exception cref="ArgumentException">An id is null, not an integer, out of the key's range, or named twice.</exception>
        private static long[] KeyValues(IReadOnlyList<object> ids, bool isLong, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(ids, parameterName);
            long[] keys = new long[ids.Count];
            HashSet<long> seen = [];
            for (int index = 0; index < ids.Count; index++)
            {
                long key = ids[index] switch
                {
                    int value => value,
                    long value => value,
                    short value => value,
                    byte value => value,
                    null => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The id at {index} is null."), parameterName),
                    object other => throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"The id at {index} is a {other.GetType().Name}; an id is an integer of the entity's key type."), parameterName),
                };

                if (!isLong && key is < int.MinValue or > int.MaxValue)
                {
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The id {key} at {index} does not fit the entity's int key."), parameterName);
                }

                if (!seen.Add(key))
                {
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The id {key} is named twice; each id is named once."), parameterName);
                }

                keys[index] = key;
            }

            return keys;
        }

        /// <summary>Binds keys as the statement's id list: an <see cref="IdList" />, or a <see cref="BigIdList" /> for a long key.</summary>
        /// <param name="draft">The draft.</param>
        /// <param name="keys">The keys.</param>
        /// <param name="isLong">Whether the key is a <see cref="long" />.</param>
        /// <returns>The parameter's name.</returns>
        private static string BindIds(StatementDraft draft, long[] keys, bool isLong)
        {
            return isLong
                ? draft.Table([.. keys.Select(static key => new BigIdList { Id = key })])
                : draft.Table([.. keys.Select(static key => new IdList { Id = checked((int)key) })]);
        }

        /// <summary>The ids a statement read back, each boxed as the entity's key type, as the receipt names them.</summary>
        /// <param name="keys">The keys.</param>
        /// <param name="isLong">Whether the key is a <see cref="long" />.</param>
        /// <returns>The set.</returns>
        private static HashSet<object> BoxedSet(IEnumerable<long> keys, bool isLong)
        {
            // Each branch boxes on its own: a conditional of int and long would widen both to long.
            return isLong
                ? [.. keys.Select(static key => (object)key)]
                : [.. keys.Select(static key => (object)checked((int)key))];
        }

        /// <summary>
        ///     Compiles the keys-only query of an entity at an ordinal: its key, under a filter, over
        ///     a key set when given, and limited to <paramref name="take" /> rows in key order when given.
        /// </summary>
        /// <param name="root">The entity's descriptor.</param>
        /// <param name="filter">The filter, or null for every row.</param>
        /// <param name="arguments">The filter's declared parameters, or null.</param>
        /// <param name="keySet">The table of ids the rows are restricted to, or null.</param>
        /// <param name="take">The most rows, or null for all.</param>
        /// <param name="ordinal">The ordinal of the statement that embeds it.</param>
        /// <returns>The compiled query.</returns>
        private CompiledQuery CompileKeys(EntityDescriptor root, FilterTree? filter, QueryArguments? arguments, string? keySet, int? take, int ordinal)
        {
            string key = root.Key.Name;
            QuerySpec spec = new()
            {
                Root = root,
                Select = key,
                Filter = filter,
                Restrictions = keySet is null ? [] : [new KeySetRestriction(key, keySet)],

                // Only an ordered query is limited; the key orders it at no cost.
                OrderBy = take is null ? null : key,
                Take = take,
            };
            return Compile(spec, arguments, ordinal);
        }

        /// <summary>
        ///     Writes a compiled key query's count, held to an expected count: the count lands in a
        ///     variable first, so the hint a flagged body needs rides the one statement that reads it.
        /// </summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="compiled">The compiled query.</param>
        /// <param name="variable">The count's variable.</param>
        /// <param name="expected">The expression the count must equal.</param>
        /// <param name="number">The error number thrown otherwise.</param>
        /// <param name="code">The error's code.</param>
        /// <param name="recompile">Whether the statement carries <c>OPTION (RECOMPILE)</c>.</param>
        private static void WriteCountCheck(StringBuilder text, CompiledQuery compiled, string variable, string expected, int number, string code, bool recompile)
        {
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {variable} int;\n")
                .Append(compiled.Prologue)
                .Append(CultureInfo.InvariantCulture, $"SELECT {variable} = COUNT(*) FROM (\n")
                .Append(compiled.Body)
                .Append(") AS [q]")
                .Append(Hint(recompile))
                .Append(";\n")
                .Append(CultureInfo.InvariantCulture, $"IF {variable} <> {expected} THROW {number}, N'{code}', 1;\n");
        }

        /// <summary>
        ///     Writes the not-found check over a list of ids: the ids no row matches under a compiled
        ///     key query (absent and hidden alike) are the statement's result set, then
        ///     <c>THROW 50404</c> when there are any.
        /// </summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="ids">The id list.</param>
        /// <param name="readable">The ids' readable rows, compiled over the list.</param>
        /// <param name="recompile">Whether the statement carries <c>OPTION (RECOMPILE)</c>.</param>
        private static void WriteNotFoundCheck(StringBuilder text, string ids, CompiledQuery readable, bool recompile)
        {
            // The count of the rows just selected decides the throw, so the body is read once.
            _ = text.Append(readable.Prologue)
                .Append(CultureInfo.InvariantCulture, $"SELECT s.[Id] FROM {ids} AS s WHERE s.[Id] NOT IN (\n")
                .Append(readable.Body)
                .Append(')')
                .Append(Hint(recompile))
                .Append(";\n")
                .Append(NotFoundThrow());
        }

        /// <summary>The throw of a not-found check whose missing ids were just selected.</summary>
        /// <returns>The line.</returns>
        private static string NotFoundThrow()
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"IF @@ROWCOUNT > 0 THROW {TellmaSqlErrors.NotFound}, N'{TellmaSqlErrors.NotFoundCode}', 1;\n");
        }

        /// <summary>The prefix of a statement's batch-local names, <c>@tb{b}_</c>, which its tree and delete text writes under.</summary>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <returns>The prefix.</returns>
        private static string LocalPrefix(int ordinal)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{ReservedSqlNames.StatementPrefix}{ordinal}_");
        }

        /// <summary>The recompile hint on its own line, ahead of a statement's terminator; empty when not recompiling.</summary>
        /// <param name="recompile">Whether the statement recompiles.</param>
        /// <returns>The hint.</returns>
        private static string Hint(bool recompile)
        {
            return recompile ? "\nOPTION (RECOMPILE)" : string.Empty;
        }

        /// <summary>The reader of a not-found check's result set: the missing ids, for the exception its throw becomes.</summary>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <returns>The reader.</returns>
        private static StatementReader MissingIdsReader(int ordinal)
        {
            return async (cursor, cancellationToken) =>
            {
                DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                await cursor.ReadMissingIdsAsync(set, cancellationToken).ConfigureAwait(false);
            };
        }

        /// <summary>
        ///     Appends a write call's statements, each compiled for the ordinal it takes, after
        ///     checking the parameter budget for all of them, so a refused call leaves the batch as
        ///     it was.
        /// </summary>
        /// <param name="statements">The statements, in ordinal order.</param>
        /// <exception cref="InvalidOperationException">The statements would pass the batch's parameter budget.</exception>
        private void AppendWrites(IReadOnlyList<WriteStatement> statements)
        {
            int added = statements.Sum(static statement => statement.Parameters);
            if (_parameterNames.Count + added > BatchAssembly.MaxParameters)
            {
                throw BatchAssembly.TooManyParameters(_parameterNames.Count + added);
            }

            foreach (WriteStatement statement in statements)
            {
                StatementDraft draft = Begin();
                try
                {
                    if (draft.Ordinal != statement.Ordinal)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture, $"A write statement compiled for ordinal {statement.Ordinal} was begun at {draft.Ordinal}."));
                    }

                    draft.EntityType = statement.EntityType;
                    statement.Bind(draft);
                    if (draft.Parameters.Count != statement.Parameters)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture, $"The write statement at ordinal {statement.Ordinal} bound {draft.Parameters.Count} parameters where it planned {statement.Parameters}."));
                    }

                    Append(draft, statement.Text, statement.Writes, idempotent: false, statement.Reader);
                }
                catch
                {
                    Discard(draft);
                    throw;
                }
            }

            // The writes stand or fall together, and their locks hold to the commit.
            _needsTransaction = true;
        }

        /// <summary>One statement of an update or a delete, its text written for the ordinal it takes.</summary>
        /// <param name="Ordinal">The ordinal it takes.</param>
        /// <param name="Text">Its text.</param>
        /// <param name="Bind">Binds its parameters and declares its names, in the order the text numbers them.</param>
        /// <param name="Parameters">How many parameters it binds.</param>
        /// <param name="Writes">The tables it writes, or null for none.</param>
        /// <param name="EntityType">The entity whose rows it addresses, which its errors name.</param>
        /// <param name="Reader">Reads its result sets, or null when it returns none.</param>
        private sealed record WriteStatement(
            int Ordinal,
            string Text,
            Action<StatementDraft> Bind,
            int Parameters,
            IReadOnlySet<TableName>? Writes,
            Type? EntityType,
            StatementReader? Reader);
    }
}
