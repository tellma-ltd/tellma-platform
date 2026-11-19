// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §6.1, §6.3 and §10.1: who owns each value, how a payload's collections replace the
    // stored rows, and the refusals of a unique value held elsewhere and of a row still referenced.
    /// <content>Ownership, collections, unique values and rows in use.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>A write-once value is refused once stored, and every server-owned value a payload sends is ignored.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_write_once_value_is_refused_once_stored_and_server_owned_values_in_a_payload_are_ignored()
        {
            Assert.SkipUnless(Serves(StackOperations.Save), $"The stack {Stack.Resource} serves no save.");
            TEntity stored = await CreateNewAsync();

            // --- Each write-once value, changed on an update: refused at its path ---
            foreach (PropertyMetadata property in Metadata.Properties.Where(static property => property.Ownership == PropertyOwnership.WriteOnce))
            {
                TEntity loaded = await LoadAsync(stored.Id);
                property.Setter(loaded, property.Getter(NewValidEntity(NextOrdinal())));
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([loaded]));
                Assert.Contains($"[0].{property.Name} {ValidationCodes.WriteOnce}", Lines(refused));
            }

            // --- Every server-owned value but the key and the stamp the check compares, perturbed on
            // an update: each is stored as it was ---
            TEntity before = await LoadAsync(stored.Id);
            TEntity payload = await LoadAsync(stored.Id);
            Change(payload, NextOrdinal());
            List<PropertyMetadata> perturbed = [];
            foreach (PropertyMetadata property in Metadata.Properties.Where(property => property.Ownership == PropertyOwnership.ServerOwned && property != Metadata.Key && property.Name != StampProperty))
            {
                if (Perturbed(property.Getter(payload)) is { } value)
                {
                    property.Setter(payload, value);
                    perturbed.Add(property);
                }
            }

            _ = await SaveAsync([payload]);
            TEntity after = await LoadAsync(stored.Id);
            Assert.All(perturbed, property => Assert.True(
                Equals(property.Getter(before), property.Getter(after)),
                $"{Stack.Resource}: the payload's {property.Name} was stored: {property.Getter(before)} became {property.Getter(after)}."));
        }

        /// <summary>
        ///     On each child collection, a payload without the collection keeps the stored rows, an
        ///     empty one deletes them, and a row whose id another entity's collection holds is not found.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_collection_keeps_its_rows_when_null_deletes_them_when_empty_and_refuses_a_foreign_child_id()
        {
            Assert.SkipUnless(Serves(StackOperations.Save), $"The stack {Stack.Resource} serves no save.");
            Assert.SkipWhen(Stack.Children.Count == 0, $"The stack {Stack.Resource} has no child collections.");

            foreach (ChildCollectionDescriptor collection in Stack.Children)
            {
                ChildCollectionMetadata metadata = Collection(collection);
                TEntity stored = await CreateNewAsync();
                int count = metadata.Getter(await LoadAsync(stored.Id))?.Count ?? 0;
                Assert.True(count > 0, $"{nameof(NewValidEntity)} gives the stack {Stack.Resource} no {collection.Property}: fill each collection with at least one row.");

                // --- Absent: the stored rows stay ---
                TEntity absent = await LoadAsync(stored.Id);
                SetRows(absent, collection, null);
                Change(absent, NextOrdinal());
                _ = await SaveAsync([absent]);
                Assert.True(count == metadata.Getter(await LoadAsync(stored.Id))?.Count, $"{Stack.Resource}: a payload without {collection.Property} changed its rows.");

                // --- A row of another entity's collection, named by its id: not found ---
                TEntity other = await CreateNewAsync();
                object foreign = metadata.Getter(await LoadAsync(other.Id))![0];
                TEntity stealing = await LoadAsync(stored.Id);
                SetRows(stealing, collection, [foreign]);
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([stealing]));
                Assert.Contains($"[0].{collection.Property}[0].{metadata.Child.Key.Name} {ValidationCodes.EntityNotFound}", Lines(refused));
                Assert.True(metadata.Getter(await LoadAsync(other.Id))?.Count > 0, $"{Stack.Resource}: the refused save moved another entity's {collection.Property}.");

                // --- Empty: the stored rows are deleted ---
                TEntity emptied = await LoadAsync(stored.Id);
                SetRows(emptied, collection, []);
                Change(emptied, NextOrdinal());
                _ = await SaveAsync([emptied]);
                Assert.True((metadata.Getter(await LoadAsync(stored.Id))?.Count ?? 0) == 0, $"{Stack.Resource}: an empty {collection.Property} kept its rows.");
            }
        }

        /// <summary>
        ///     A unique or natural-key value another row holds is refused at its path, and so is a
        ///     unique value of a collection's rows that another row of the payload repeats.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_unique_value_another_row_holds_is_refused_at_its_path()
        {
            Assert.SkipUnless(Serves(StackOperations.Save), $"The stack {Stack.Resource} serves no save.");
            List<PropertyMetadata> unique = Unique(Metadata);
            List<(ChildCollectionDescriptor Collection, PropertyMetadata Property)> childUnique = [.. Stack.Children
                .SelectMany(collection => Unique(Collection(collection).Child).Select(property => (collection, property)))];
            Assert.SkipWhen(unique.Count == 0 && childUnique.Count == 0, $"The stack {Stack.Resource} has no unique values.");

            // --- Each root value, taken from a stored row ---
            TEntity stored = await CreateNewAsync();
            foreach (PropertyMetadata property in unique)
            {
                TEntity taken = NewValidEntity(NextOrdinal());
                property.Setter(taken, property.Getter(stored));
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([taken]));
                Assert.Contains($"[0].{property.Name} {ValidationCodes.Unique}", Lines(refused));
            }

            // --- Each child value, repeated by a second row of the same collection ---
            foreach ((ChildCollectionDescriptor collection, PropertyMetadata property) in childUnique)
            {
                ChildCollectionMetadata metadata = Collection(collection);
                TEntity entity = NewValidEntity(NextOrdinal());
                object first = metadata.Getter(entity)![0];
                object second = metadata.Getter(NewValidEntity(NextOrdinal()))![0];
                property.Setter(second, property.Getter(first));
                SetRows(entity, collection, [first, second]);
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([entity]));
                Assert.Contains(refused.Errors, error => error.Code == ValidationCodes.Unique && error.Path.ToString().StartsWith($"[0].{collection.Property}[", StringComparison.Ordinal) && error.Path.ToString().EndsWith($".{property.Name}", StringComparison.Ordinal));
            }
        }

        /// <summary>A delete of a row another entity's foreign key still references is refused as in use, naming that entity and key.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_row_still_referenced_is_refused_as_in_use_naming_the_referencing_entity_and_key()
        {
            Assert.SkipUnless(Serves(StackOperations.Delete), $"The stack {Stack.Resource} serves no delete.");
            List<(EntityMetadata Entity, ReferenceMetadata Reference)> incoming = Incoming();
            Assert.SkipWhen(incoming.Count == 0, $"No entity references the stack {Stack.Resource}.");

            foreach ((EntityMetadata entity, ReferenceMetadata reference) in incoming)
            {
                TEntity target = await CreateNewAsync();
                await ReferToAsync(reference, entity, target);

                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => AsWriterAsync(service => service.DeleteByIdsAsync([target.Id], null, Token)));
                ValidationError error = Assert.Single(refused.Errors);
                Assert.Equal(("Ids[0]", ValidationCodes.FkInUse), (error.Path.ToString(), error.Code));
                Assert.Equal((entity.Name, reference.ForeignKey.Name), (Convert.ToString(error.Arguments["entity"], CultureInfo.InvariantCulture), Convert.ToString(error.Arguments["property"], CultureInfo.InvariantCulture)));
                Assert.True(await ExistsAsync(target.Id), $"{Stack.Resource}: the refused delete removed the referenced row.");
            }
        }

        /// <summary>The unique values a client writes on an entity: unique or natural keys, never the key or a blob column.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The properties.</returns>
        private static List<PropertyMetadata> Unique(EntityMetadata entity)
        {
            HashSet<PropertyMetadata> blobs = new(entity.BlobReferences.Select(static blob => blob.Property), ReferenceEqualityComparer.Instance);
            HashSet<PropertyMetadata> natural = new(entity.NaturalKeys.Where(static key => key.IsDeclared).Select(static key => key.Property), ReferenceEqualityComparer.Instance);
            return [.. entity.Properties.Where(property =>
                (property.IsUnique || natural.Contains(property))
                && !ReferenceEquals(property, entity.Key)
                && !blobs.Contains(property)
                && property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce)];
        }

        /// <summary>
        ///     A value of a server-owned property's type that differs from the one given; null for a
        ///     type the suite does not perturb.
        /// </summary>
        /// <param name="value">The stored value.</param>
        /// <returns>The perturbed value.</returns>
        private static object? Perturbed(object? value)
        {
            return value switch
            {
                int number => number + 7,
                long number => number + 7,
                bool flag => !flag,
                string text => text + "-sent",
                DateTimeOffset moment => moment.AddDays(-3),
                DateTime moment => moment.AddDays(-3),
                Guid => Guid.NewGuid(),
                _ => null,
            };
        }

        /// <summary>
        ///     Every foreign key in the model whose target is the stack's entity and whose row outlives
        ///     a delete of its target: never a collection's parent key, which the delete takes with it,
        ///     a sibling's shared key, a blob column or a value the server owns.
        /// </summary>
        /// <returns>Each referencing entity and its reference.</returns>
        private List<(EntityMetadata Entity, ReferenceMetadata Reference)> Incoming()
        {
            IEntityMetadataProvider provider = Harness.Services.GetRequiredService<IEntityMetadataProvider>();
            HashSet<PropertyMetadata> parentKeys = new(provider.All.SelectMany(static entity => entity.Children).Select(static collection => collection.ParentKey), ReferenceEqualityComparer.Instance);
            List<(EntityMetadata Entity, ReferenceMetadata Reference)> incoming = [];
            foreach (EntityMetadata entity in provider.All)
            {
                HashSet<PropertyMetadata> blobs = new(entity.BlobReferences.Select(static blob => blob.Property), ReferenceEqualityComparer.Instance);
                foreach (ReferenceMetadata reference in entity.References.Where(reference => reference.Target.ClrType == Stack.EntityType))
                {
                    PropertyMetadata key = reference.ForeignKey;
                    if (!parentKeys.Contains(key) && !ReferenceEquals(key, entity.Key) && !blobs.Contains(key) && key.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce)
                    {
                        incoming.Add((entity, reference));
                    }
                }
            }

            return incoming;
        }
    }
}
