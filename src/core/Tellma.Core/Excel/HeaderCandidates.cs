// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Excel
{
    // Spec 0019 §8.1 step 2: every header the editable or display export could write for each column
    // path, importable or not, plus the technical paths. Labels come from the scope's culture; a
    // multilingual group's label is rendered bare and suffixed here with every catalogued language's
    // symbol, so a file from a tenant of other languages, or of more of them, still matches. Reference
    // headers pair the navigation's label with every scalar of the target, both halves in one culture.
    /// <summary>The header candidates of a sheet's entity in the culture of the current localization scope.</summary>
    internal static class HeaderCandidates
    {
        /// <summary>Every header an export could write for a sheet's entity, with the path each names.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="labels">The labels, rendering in the scope's culture with no content languages.</param>
        /// <param name="catalogue">Every catalogued language, whose symbols suffix a multilingual group's label.</param>
        /// <returns>The candidates, normalised.</returns>
        public static IReadOnlyList<HeaderCandidate> Of(EntityMetadata entity, ChildCollectionMetadata? collection, ILabelProvider labels, IReadOnlyList<LanguageInfo> catalogue)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(labels);
            ArgumentNullException.ThrowIfNull(catalogue);
            Builder builder = new();
            Type type = entity.ClrType;

            // --- The identity, by its name and its label; the parent sheet's stamp, by its name ---
            builder.Technical(ImportPaths.Id, ImportPaths.Id);
            builder.Labelled(labels.PropertyLabel(type, entity.Key.Name), ImportPaths.Id);
            if (collection is null && entity.Properties.Any(static property => property.Name == ExportPlanner.StampSource))
            {
                builder.Technical(ImportPaths.Stamp, ImportPaths.Stamp);
            }

            // --- A child sheet's link to its owner row: always in the owner's surrogate ---
            if (collection is not null)
            {
                string navigation = ImportPaths.ParentNavigation(collection);
                string link = $"{navigation}.{ImportPaths.Id}";
                string label = labels.NavigationLabel(type, navigation);
                builder.Technical(link, link);
                builder.Technical(collection.ParentKey.Name, link);
                builder.Labelled(Reference(label, ImportPaths.Id), link);
                if (entity.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name) is ReferenceMetadata owner)
                {
                    builder.Labelled(Reference(label, labels.PropertyLabel(owner.Target.ClrType, owner.Target.Key.Name)), link);
                }
            }

            // --- Each scalar and each multilingual slot ---
            foreach (PropertyMetadata property in entity.Properties)
            {
                if (property.Name == entity.Key.Name
                    || (collection is not null && property.Name == collection.ParentKey.Name)
                    || entity.References.Any(reference => reference.ForeignKey.Name == property.Name))
                {
                    continue;
                }

                builder.Technical(property.Name, property.Name);
                if (ImportPaths.Slot(entity, property.Name) is (MultilingualGroup group, int slot))
                {
                    // A twin's export header is its group's label with its language's symbol, listed at the primary.
                    if (slot == 1)
                    {
                        builder.Group(labels.PropertyLabel(type, group.Primary.Name), group.Primary.Name, catalogue);
                    }

                    continue;
                }

                builder.Labelled(labels.PropertyLabel(type, property.Name), property.Name);
            }

            // --- Each reference other than the parent link: its surrogate, its bare navigation, and every scalar of its target ---
            foreach (ReferenceMetadata reference in entity.References)
            {
                if (collection is not null && reference.ForeignKey.Name == collection.ParentKey.Name)
                {
                    continue;
                }

                AddReference(builder, entity, reference, labels, catalogue);
            }

            return builder.Candidates;
        }

        /// <summary>A reference's candidates.</summary>
        /// <param name="builder">Receives them.</param>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="reference">The reference.</param>
        /// <param name="labels">The labels.</param>
        /// <param name="catalogue">Every catalogued language.</param>
        private static void AddReference(Builder builder, EntityMetadata entity, ReferenceMetadata reference, ILabelProvider labels, IReadOnlyList<LanguageInfo> catalogue)
        {
            string navigation = reference.Navigation;
            EntityMetadata target = reference.Target;
            string label = labels.NavigationLabel(entity.ClrType, navigation);

            // --- The surrogate: <Navigation>.Id, the bare foreign key, and the labelled forms ---
            string surrogate = $"{navigation}.{ImportPaths.Id}";
            builder.Technical(surrogate, surrogate);
            builder.Technical(reference.ForeignKey.Name, surrogate);
            builder.Technical(Reference(label, target.Key.Name), surrogate);
            builder.Labelled(Reference(label, ImportPaths.Id), surrogate);
            builder.Labelled(Reference(label, labels.PropertyLabel(target.ClrType, target.Key.Name)), surrogate);

            // --- The bare navigation: the display name a display export writes ---
            builder.Technical(navigation, navigation);
            builder.Labelled(label, navigation);

            // --- Every scalar of the target that fits a cell: by its path, its name and its label ---
            foreach (PropertyMetadata key in target.Properties)
            {
                if (key.Name == target.Key.Name || !ExportPlanner.IsKeyable(target, key))
                {
                    continue;
                }

                string path = $"{navigation}.{key.Name}";
                builder.Technical(path, path);
                builder.Technical(Reference(label, key.Name), path);
                if (ImportPaths.Slot(target, key.Name) is (MultilingualGroup group, int slot))
                {
                    if (slot == 1)
                    {
                        builder.Group(Reference(label, labels.PropertyLabel(target.ClrType, group.Primary.Name)), path, catalogue);
                    }

                    continue;
                }

                builder.Labelled(Reference(label, labels.PropertyLabel(target.ClrType, key.Name)), path);
            }
        }

        /// <summary>A reference header: the navigation's label and the key's, as the export joins them.</summary>
        /// <param name="navigation">The navigation's label.</param>
        /// <param name="key">The key's label or name.</param>
        /// <returns>The header.</returns>
        private static string Reference(string navigation, string key)
        {
            return string.Concat(navigation, ExportPlanner.LabelSeparator, key);
        }

        /// <summary>Collects candidates, normalised.</summary>
        private sealed class Builder
        {
            /// <summary>The candidates so far.</summary>
            public List<HeaderCandidate> Candidates { get; } = [];

            /// <summary>Adds a header by its technical form alone.</summary>
            /// <param name="header">The header.</param>
            /// <param name="path">The path it names.</param>
            /// <param name="language">The language it names; null for none.</param>
            public void Technical(string header, string path, string? language = null)
            {
                Candidates.Add(new HeaderCandidate(HeaderIndex.Normalize(header), new HeaderTarget(path, language)));
            }

            /// <summary>Adds a labelled header, and its disambiguated form <c>&lt;Label&gt; (&lt;Path&gt;)</c>.</summary>
            /// <param name="header">The header.</param>
            /// <param name="path">The path it names.</param>
            /// <param name="language">The language it names; null for none.</param>
            public void Labelled(string header, string path, string? language = null)
            {
                Technical(header, path, language);
                Technical($"{header} ({path})", path, language);
            }

            /// <summary>Adds a multilingual group's headers: its bare label for the primary slot, and the label with each catalogued language's symbol.</summary>
            /// <param name="label">The group's label, bare.</param>
            /// <param name="path">The group's language-relative path.</param>
            /// <param name="catalogue">Every catalogued language.</param>
            public void Group(string label, string path, IReadOnlyList<LanguageInfo> catalogue)
            {
                Labelled(label, path);
                foreach (LanguageInfo language in catalogue)
                {
                    Labelled($"{label} ({language.Symbol})", path, language.Code);
                }
            }
        }
    }
}
