// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The blob kinds of the realised composition, built once, on first use, from the entity
    ///     metadata and the stack registry: every <c>[BlobReference]</c> column of a stack's root and
    ///     of the child entities of its aggregate, at any depth.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The policy is the metadata's, which already applied the composition's transforms, so
    ///         the registry and every metadata consumer agree on it. The owner fields come from the
    ///         walk (spec 0017 §2.4): a root's column is governed by the stack's resource; a child's by
    ///         the child's own query resource when the stack registers one for it, else by the
    ///         stack's, reached from the child through its dotted path of parent navigations.
    ///     </para>
    ///     <para>
    ///         A model whose metadata failed, or a stack the registry could not build, contributes no
    ///         kinds: the entity-metadata and stacks checks report those, and the gate fails anyway.
    ///     </para>
    /// </remarks>
    internal sealed class BlobKindRegistry : IBlobKindRegistry
    {
        // The kinds, built on first use.
        private readonly Lazy<Contents> _contents;

        /// <summary>Creates the registry over the composition's metadata and stacks.</summary>
        /// <param name="metadata">The entity metadata source.</param>
        /// <param name="stacks">The stack registry.</param>
        public BlobKindRegistry(EntityMetadataSource metadata, IStackRegistry stacks)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(stacks);
            _contents = new(() => Build(metadata, stacks), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public IReadOnlyList<BlobKindDescriptor> All => _contents.Value.All;

        /// <inheritdoc />
        public BlobKindDescriptor? Find(string kind)
        {
            ArgumentNullException.ThrowIfNull(kind);
            return _contents.Value.ByKind.GetValueOrDefault(kind);
        }

        /// <summary>Walks every stack and its child collections, collecting each blob-reference column once.</summary>
        /// <param name="metadata">The entity metadata source.</param>
        /// <param name="stacks">The stack registry.</param>
        /// <returns>The kinds.</returns>
        private static Contents Build(EntityMetadataSource metadata, IStackRegistry stacks)
        {
            List<BlobKindDescriptor> all = [];
            Dictionary<string, BlobKindDescriptor> byKind = new(StringComparer.Ordinal);
            if (metadata.Build.Provider is not IEntityMetadataProvider provider)
            {
                return new Contents(all, byKind);
            }

            // A child's own query surface, by its entity type: it governs the child's columns.
            Dictionary<Type, ChildQueryDescriptor> childQueries = [];
            foreach (ChildQueryDescriptor child in stacks.ChildQueries)
            {
                _ = childQueries.TryAdd(child.EntityType, child);
            }

            foreach (StackDescriptor stack in stacks.Stacks)
            {
                if (provider.All.FirstOrDefault(entity => entity.ClrType == stack.EntityType) is EntityMetadata root)
                {
                    Visit(root, stack, [], childQueries, all, byKind);
                }
            }

            return new Contents(all.AsReadOnly(), byKind);
        }

        /// <summary>Collects an entity's blob-reference columns, then its children's, depth first.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="stack">The stack whose aggregate holds it.</param>
        /// <param name="pathToRoot">The navigations from the entity up to the stack's root, nearest first; empty for the root.</param>
        /// <param name="childQueries">The child query surfaces by entity type.</param>
        /// <param name="all">The kinds collected so far.</param>
        /// <param name="byKind">The kinds collected so far, by name.</param>
        private static void Visit(
            EntityMetadata entity,
            StackDescriptor stack,
            IReadOnlyList<string> pathToRoot,
            Dictionary<Type, ChildQueryDescriptor> childQueries,
            List<BlobKindDescriptor> all,
            Dictionary<string, BlobKindDescriptor> byKind)
        {
            // --- The governing securable: the stack's for the root; a child's own when it has one ---
            (string ownerResource, string ownerPath) = pathToRoot.Count == 0
                ? (stack.Resource, string.Empty)
                : childQueries.TryGetValue(entity.ClrType, out ChildQueryDescriptor? own)
                    ? (own.Resource, string.Empty)
                    : (stack.Resource, string.Join('.', pathToRoot));

            foreach (BlobReferenceMetadata reference in entity.BlobReferences)
            {
                // The metadata guarantees one property per kind; a second sighting is the same column
                // reached again, which a model with a shared child class could produce.
                BlobKindDescriptor descriptor = new(
                    reference.Kind, reference.Policy, ownerResource, entity.Name, entity.Table, reference.Property.Column, ownerPath);
                if (byKind.TryAdd(reference.Kind, descriptor))
                {
                    all.Add(descriptor);
                }
            }

            // --- The children, each one navigation further from the root ---
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                ReferenceMetadata? parent = collection.Child.References.FirstOrDefault(
                    reference => ReferenceEquals(reference.ForeignKey, collection.ParentKey) || reference.ForeignKey.Name == collection.ParentKey.Name);
                if (parent is null)
                {
                    // The metadata pairs every collection with its parent key's navigation; a child
                    // without one cannot be governed through its parent and is reported there.
                    continue;
                }

                Visit(collection.Child, stack, [parent.Navigation, .. pathToRoot], childQueries, all, byKind);
            }
        }

        /// <summary>The registry's contents.</summary>
        /// <param name="All">Every kind, in walk order.</param>
        /// <param name="ByKind">Every kind, by name.</param>
        private sealed record Contents(IReadOnlyList<BlobKindDescriptor> All, IReadOnlyDictionary<string, BlobKindDescriptor> ByKind);
    }
}
