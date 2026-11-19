// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;

namespace Tellma.Core.Localization
{
    // The keys are spec 0013 §10.1's; the securables' label chains are those of the client
    // metadata, so a refusal names a permission exactly as the permission editor shows it.
    /// <summary>
    ///     Renders a shared resource by its key alone, whichever composed feature declares it, and the
    ///     labels of a securable's resource and action: the texts a response carries for readers
    ///     without a string pack.
    /// </summary>
    /// <remarks>
    ///     A key renders through the localizer of the resources that own it, so it is looked up in
    ///     the reader's culture (the flow's entered localization scope, else the current UI culture)
    ///     through that culture's parents to English, and formatted as an ICU message with its
    ///     arguments.
    /// </remarks>
    /// <param name="strings">The merged shared string resources, which name each key's owner.</param>
    /// <param name="localizers">Creates the localizer of each owner's resources.</param>
    /// <param name="metadata">The model's entity metadata, which says which resources are entities.</param>
    internal sealed class ResourceTexts(StringPackComposer strings, IStringLocalizerFactory localizers, IEntityMetadataProvider metadata)
    {
        /// <summary>Whether a composed feature's neutral resources declare a key.</summary>
        /// <param name="key">The key.</param>
        /// <returns>True when one does.</returns>
        public bool Declares(string key)
        {
            return strings.Declares(key);
        }

        /// <summary>Renders a key with its arguments in the reader's culture.</summary>
        /// <param name="key">The key.</param>
        /// <param name="arguments">The named arguments; null when the text takes none.</param>
        /// <returns>The text, or null when no composed feature declares the key.</returns>
        public string? Render(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (strings.OwnerOf(key) is not StringResourceSource owner)
            {
                return null;
            }

            // --- A source read in memory has no assembly to localize from: its English text as it is ---
            if (owner.AssemblyName is not string assembly)
            {
                return strings.English[key];
            }

            IStringLocalizer localizer = localizers.Create(owner.Name, assembly);
            LocalizedString text = arguments is null ? localizer[key] : localizer[key, arguments];
            return text.Value;
        }

        /// <summary>The label of a securable's resource: an entity's plural label, a settings category's, or the resource's own.</summary>
        /// <param name="resource">The resource, such as <c>gl.Center</c>.</param>
        /// <returns>The label in the reader's culture.</returns>
        public string ResourceLabel(string resource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            (string[] chain, string fallback) = SecurablesClientAssetContributor.ResourceLabelChain(resource, metadata);
            return First(chain) ?? fallback;
        }

        /// <summary>The label of a securable's action under its resource: the resource's own action label, else the shared one.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action, such as <c>Save</c> or <c>export/start</c>.</param>
        /// <returns>The label in the reader's culture.</returns>
        public string ActionLabel(string resource, string action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            string segment = StringKeyConventions.ActionSegment(action);
            string[] chain =
            [
                StringKeyConventions.KeyPrefix(resource) + StringKeyConventions.ActionInfix + segment,
                StringKeyConventions.ActionPrefix + segment,
            ];
            return First(chain) ?? NameHumanizer.Humanize(action);
        }

        /// <summary>The text of the first key of a chain a composed feature declares.</summary>
        /// <param name="chain">The keys, most specific first.</param>
        /// <returns>The text, or null when none is declared.</returns>
        private string? First(string[] chain)
        {
            foreach (string key in chain)
            {
                if (Render(key) is string text)
                {
                    return text;
                }
            }

            return null;
        }
    }
}
