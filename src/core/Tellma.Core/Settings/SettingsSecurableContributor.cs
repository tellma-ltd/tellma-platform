// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Settings
{
    // The securables are spec 0013 §5.5's: Read and Save per settings resource, with no filter root,
    // since no row is the target. core.Settings.General covers the typed row and the general
    // category's keys, and its Save is sensitive (spec 0014's step-up); every other category's
    // resource is core.Settings. and the category verbatim, and is not sensitive.
    /// <summary>Registers <c>Read</c> and <c>Save</c> of the typed settings and of every settings category.</summary>
    /// <param name="keys">The setting keys, whose categories name the resources.</param>
    internal sealed class SettingsSecurableContributor(ISettingKeyRegistry keys) : ISecurableContributor
    {
        /// <inheritdoc />
        public void Contribute(SecurableRegistryBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The typed row's resource, registered whether or not a general key is declared ---
            Add(builder, CoreResources.SettingsGeneral, sensitive: true);

            // --- Each other category's resource ---
            foreach (SettingCategoryDescriptor category in keys.Categories)
            {
                if (!SettingKeyRegistry.IsGeneral(category.Name))
                {
                    Add(builder, category.Resource, sensitive: false);
                }
            }
        }

        /// <summary>Registers a resource's two actions.</summary>
        /// <param name="builder">The builder.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="sensitive">Whether its <c>Save</c> asks for a recent sign-in.</param>
        private static void Add(SecurableRegistryBuilder builder, string resource, bool sensitive)
        {
            _ = builder.Add(resource, AccessActions.Read, filterRoot: null);
            _ = builder.Add(resource, AccessActions.Save, filterRoot: null, isSensitive: sensitive);
        }
    }
}
