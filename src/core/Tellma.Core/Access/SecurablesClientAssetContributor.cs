// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Localization;

namespace Tellma.Core.Access
{
    // The section is spec 0014 §10's; the label chains are spec 0032 §12.4's, keyed as spec 0013
    // §10.1 names keys.
    /// <summary>
    ///     Writes the <c>securables</c> section of the Core chunk of the client metadata: one
    ///     <see cref="ClientSecurable" /> per registered securable, in the registry's order, with the
    ///     key of each label resolved, so the role editor renders every label with no request.
    /// </summary>
    /// <remarks>
    ///     <para>Each label takes the first key of its chain that a neutral shared resource declares:</para>
    ///     <list type="bullet">
    ///         <item>an entity's resource: <c>&lt;Schema&gt;_&lt;Entity&gt;_Plural</c>, then <c>&lt;Schema&gt;_&lt;Entity&gt;</c>;</item>
    ///         <item>
    ///             a settings resource: <c>SettingCategory_general</c> for <c>core.Settings.General</c>,
    ///             <c>SettingCategory_&lt;category&gt;</c> for <c>core.Settings.&lt;category&gt;</c>;
    ///         </item>
    ///         <item>any other resource: <c>Securable_&lt;resource&gt;</c>, its dots written <c>_</c>;</item>
    ///         <item>an action: <c>&lt;Schema&gt;_&lt;Resource&gt;_Action_&lt;Action&gt;</c>, then <c>Action_&lt;Action&gt;</c>;</item>
    ///         <item>a feature: <c>Feature_&lt;feature&gt;</c>, its dots written <c>_</c>.</item>
    ///     </list>
    ///     <para>
    ///         With no key of a chain declared, the label carries the chain's first key and humanized
    ///         English: the resource's last segment (a settings category's name), the action, the
    ///         feature's name.
    ///     </para>
    /// </remarks>
    /// <param name="registry">The securable registry.</param>
    /// <param name="metadata">The model's entity metadata, which says which resources are entities.</param>
    /// <param name="strings">The keys the neutral shared resources declare.</param>
    internal sealed class SecurablesClientAssetContributor(ISecurableRegistry registry, IEntityMetadataProvider metadata, INeutralStringKeys strings) : IClientAssetContributor
    {
        /// <summary>The section's name.</summary>
        public const string Section = "securables";

        /// <summary>The category whose resource is <c>core.Settings.General</c>.</summary>
        private const string GeneralCategory = "general";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The platform's JSON rules: camelCase members, nulls omitted, so a fallback is present
            // only when no resource declares its label's key.
            List<ClientSecurable> securables = [.. registry.All.Select(Describe)];
            context.Section(ClientMetadataChunk.Core, Section, writer => JsonSerializer.Serialize(writer, securables, TellmaJson.Options));
        }

        /// <summary>One securable with its label keys.</summary>
        /// <param name="securable">The securable.</param>
        /// <returns>Its client description.</returns>
        internal ClientSecurable Describe(SecurableDescriptor securable)
        {
            (string resourceKey, string? resourceFallback) = ResourceLabel(securable.Resource);
            (string actionKey, string? actionFallback) = Resolve(
                [$"{KeyPrefix(securable.Resource)}_Action_{securable.Action}", $"Action_{securable.Action}"],
                NameHumanizer.Humanize(securable.Action));
            (string featureKey, string? featureFallback) = Resolve([$"Feature_{securable.Feature.Replace('.', '_')}"], NameHumanizer.Humanize(securable.Feature));

            return new ClientSecurable(
                securable.Resource,
                securable.Action,
                securable.FilterRoot,
                securable.IsSensitive,
                securable.Feature,
                securable.Owner,
                resourceKey,
                resourceFallback,
                actionKey,
                actionFallback,
                featureKey,
                featureFallback);
        }

        /// <summary>
        ///     The prefix every key of a resource starts with: the resource with its schema, its first
        ///     segment, capitalized and its dots written <c>_</c> (<c>gl.Center</c> is <c>Gl_Center</c>).
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The prefix.</returns>
        internal static string KeyPrefix(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string underscored = resource.Replace('.', '_');
            return underscored.Length == 0 ? underscored : char.ToUpperInvariant(underscored[0]) + underscored[1..];
        }

        /// <summary>The key of a resource's label, by the chain its kind takes, and its fallback.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The key, and the fallback when no resource declares a key of the chain.</returns>
        private (string Key, string? Fallback) ResourceLabel(string resource)
        {
            // --- An entity: its plural label, then its singular one ---
            if (metadata.All.Any(entity => string.Equals(entity.Name, resource, StringComparison.OrdinalIgnoreCase)))
            {
                string prefix = KeyPrefix(resource);
                return Resolve([$"{prefix}_Plural", prefix], NameHumanizer.Humanize(LastSegment(resource)));
            }

            // --- A settings category: its tab's label; the general settings are the general category ---
            if (resource.StartsWith(CoreResources.SettingsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string category = resource[CoreResources.SettingsPrefix.Length..];
                if (string.Equals(resource, CoreResources.SettingsGeneral, StringComparison.OrdinalIgnoreCase))
                {
                    category = GeneralCategory;
                }

                if (!category.Contains('.', StringComparison.Ordinal))
                {
                    return Resolve([$"SettingCategory_{category}"], NameHumanizer.Humanize(category));
                }
            }

            // --- Any other resource: its own key ---
            return Resolve([$"Securable_{resource.Replace('.', '_')}"], NameHumanizer.Humanize(LastSegment(resource)));
        }

        /// <summary>The first key of a chain a neutral shared resource declares; else the chain's first key with a fallback.</summary>
        /// <param name="chain">The keys, most specific first.</param>
        /// <param name="fallback">The label when no key is declared.</param>
        /// <returns>The key, and the fallback when no key is declared.</returns>
        private (string Key, string? Fallback) Resolve(string[] chain, string fallback)
        {
            foreach (string key in chain)
            {
                if (strings.Declares(key))
                {
                    return (key, null);
                }
            }

            return (chain[0], fallback);
        }

        /// <summary>A dotted name's last segment.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The text after its last dot.</returns>
        private static string LastSegment(string name)
        {
            return name[(name.LastIndexOf('.') + 1)..];
        }
    }
}
