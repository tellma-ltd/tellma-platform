// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Saves
{
    /// <summary>
    ///     The save emitter's shared part: each root entity's plan, and its text cached per plan and
    ///     recompile hint with the statement's ordinal filled in on use. A singleton.
    /// </summary>
    /// <param name="metadata">The running model's metadata.</param>
    /// <param name="binder">The table-type binder.</param>
    /// <param name="options">The execution settings: the large-batch threshold and the conflict-message cap.</param>
    internal sealed class SaveEmitter(IEntityMetadataProvider metadata, TableTypeBinder binder, IOptions<DataOptions> options)
    {
        /// <summary>The fixed JSON text around a ten-digit count in the conflict message.</summary>
        private const int MessageOverhead = 48;

        /// <summary>The longest message <c>THROW</c> takes.</summary>
        private const int MessageLength = 2048;

        /// <summary>The plans, by root entity leaf.</summary>
        private readonly ConcurrentDictionary<Type, SavePlan> _plans = new();

        /// <summary>The rows above which a table-valued parameter makes every data-modifying statement recompile.</summary>
        public int LargeBatchThreshold => options.Value.LargeBatchThreshold;

        /// <summary>
        ///     The most ids of each list the conflict message names: the configured cap, or fewer when
        ///     the key's widest ids would overflow <c>THROW</c>'s message.
        /// </summary>
        /// <param name="isLong">Whether the key is a <see cref="long" />.</param>
        /// <returns>90 for an <see cref="int" /> key and 50 for a <see cref="long" /> key, at the default cap.</returns>
        public int ConflictCap(bool isLong)
        {
            int digits = isLong ? 19 : 10;
            return Math.Min(options.Value.ConflictIdsInMessage, (MessageLength - MessageOverhead) / 2 / (digits + 1));
        }

        /// <summary>The plan of a root entity.</summary>
        /// <param name="entityType">The entity, or a base its leaf replaces.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="InvalidOperationException">The entity is not top-level, so it cannot be saved as a root.</exception>
        public SavePlan Plan(Type entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            EntityMetadata root = metadata.Get(entityType);
            return root.IsTopLevel
                ? _plans.GetOrAdd(root.ClrType, _ => new SavePlan(root, binder))
                : throw new InvalidOperationException(
                    $"{root.Name} is not a top-level entity, so it is saved with its parent, as a child collection, never as a root.");
        }

        /// <summary>The text of a plan for the statement at an ordinal.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="recompile">Whether every data-modifying statement carries <c>OPTION (RECOMPILE)</c>.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <returns>The text.</returns>
        public string Text(SavePlan plan, bool recompile, int ordinal)
        {
            ArgumentNullException.ThrowIfNull(plan);
            string? template = plan.CachedText(recompile);
            if (template is null)
            {
                template = SaveText.Write(plan, recompile, ConflictCap(plan.Root.IsLong));
                plan.Cache(recompile, template);
            }

            return template.Replace(
                SaveText.Placeholder,
                string.Create(CultureInfo.InvariantCulture, $"{ReservedSqlNames.StatementPrefix}{ordinal}_"),
                StringComparison.Ordinal);
        }

        /// <summary>Whether a result set is a concurrency guard's conflict rows, by its columns.</summary>
        /// <param name="set">The reader, on the set.</param>
        /// <returns>True for the conflict rows.</returns>
        public static bool IsConflictSet(DbDataReader set)
        {
            ArgumentNullException.ThrowIfNull(set);
            return set.FieldCount == 7 && string.Equals(set.GetName(1), "Reason", StringComparison.Ordinal);
        }

        /// <summary>Reads the stamped ids of a save's root update.</summary>
        /// <param name="set">The reader, on the set.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The ids.</returns>
        public static async ValueTask<HashSet<long>> ReadIdsAsync(DbDataReader set, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(set);
            HashSet<long> ids = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = ids.Add(Convert.ToInt64(set.GetValue(0), CultureInfo.InvariantCulture));
            }

            return ids;
        }
    }
}
