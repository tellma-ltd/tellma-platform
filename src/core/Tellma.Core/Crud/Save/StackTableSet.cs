// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.7 step 4 and spec 0014 §8.3: the access guards' lock and invariants join a
    // persist whose stack or declared writes meet a security table; a stack writes one when its
    // root or any child collection at any depth maps to one.
    /// <summary>The tables a stack's save writes, and whether any is a security table the access guards hold to their invariants.</summary>
    internal static class StackTableSet
    {
        /// <summary>The security tables whose writes the access guards hold to their invariants.</summary>
        private static readonly TableName[] SecurityTables =
        [
            new("core", "Users"),
            new("core", "Roles"),
            new("core", "RoleMemberships"),
            new("core", "Permissions"),
        ];

        /// <summary>The tables of an entity and of every entity its collections hold, at every depth.</summary>
        /// <param name="root">The entity.</param>
        /// <returns>The tables.</returns>
        public static HashSet<TableName> Of(EntityMetadata root)
        {
            ArgumentNullException.ThrowIfNull(root);
            HashSet<TableName> tables = [];
            Stack<EntityMetadata> pending = new([root]);
            while (pending.TryPop(out EntityMetadata? entity))
            {
                if (tables.Add(entity.Table))
                {
                    foreach (ChildCollectionMetadata collection in entity.Children)
                    {
                        pending.Push(collection.Child);
                    }
                }
            }

            return tables;
        }

        /// <summary>Whether any of the tables is a security table.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>True when one is.</returns>
        public static bool WritesSecurityTable(IEnumerable<TableName> tables)
        {
            ArgumentNullException.ThrowIfNull(tables);
            return tables.Any(static table => SecurityTables.Any(security =>
                string.Equals(security.Schema, table.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(security.Name, table.Name, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
