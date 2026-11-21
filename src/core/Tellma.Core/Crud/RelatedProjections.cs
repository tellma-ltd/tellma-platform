// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud
{
    // The related projection is spec 0012 §2.5's, as the target's stack states it (spec 0015 §2.2):
    // the stack's or child surface's related select, else the derived one, held to the columns the
    // batch's schema declares.
    /// <summary>The columns a read of an entity's related rows projects, for a service that fills related entities itself.</summary>
    internal static class RelatedProjections
    {
        /// <summary>The columns of an entity's related select its schema entity declares, its key first.</summary>
        /// <param name="stacks">The stack registry, whose stacks state the related selects.</param>
        /// <param name="entity">The entity's leaf.</param>
        /// <param name="schema">The query schema the read runs under.</param>
        /// <returns>The columns.</returns>
        /// <exception cref="InvalidOperationException">The schema declares no entity of the leaf.</exception>
        public static OrderedNameSet Of(IStackRegistry stacks, EntityMetadata entity, QueryexSchema schema)
        {
            ArgumentNullException.ThrowIfNull(stacks);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(schema);

            EntityDescriptor descriptor = schema.FindEntity(entity.Name)
                ?? throw new InvalidOperationException($"The query schema declares no entity {entity.Name}, so its related rows cannot be read.");
            IReadOnlySet<string> related = stacks.Find(entity.Name)?.RelatedSelect
                ?? stacks.FindChildQuery(entity.Name)?.RelatedSelect
                ?? StackDescriptorBuilder.RelatedSelect(entity);

            // A multilingual twin the tenant's languages leave out is not in the schema.
            return new OrderedNameSet([entity.Key.Name, .. related.Where(column => SchemaLookup.Property(descriptor, column) is not null)]);
        }
    }
}
