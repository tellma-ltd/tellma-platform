// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="SettingKeysContributionItem" />s: each declaring type once, in
    ///     realisation order, which is the order the setting-key registry lists their keys in. The
    ///     keys themselves are the startup gate's to judge.
    /// </summary>
    internal sealed class SettingKeysContributionRealizer : IContributionRealizer<SettingKeysContributionItem>
    {
        /// <inheritdoc />
        public void Realize(SettingKeysContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The type must be one whose static fields can be read: closed, and not an interface ---
            Type? declaringType = item.DeclaringType;
            if (declaringType is null || declaringType.ContainsGenericParameters || declaringType.IsInterface)
            {
                string described = declaringType is null ? "no type" : TypeDisplay.Qualified(declaringType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the setting keys of {described}, which is not a closed class or struct.",
                    "Declare the keys as public static readonly fields of a closed class, and contribute it: contribution.SettingKeys(typeof(Holder))."));
                return;
            }

            // --- Once per type: a second contribution adds no key twice ---
            if (!context.Record.SettingKeyTypes.Exists(existing => existing.Type == declaringType))
            {
                context.Record.SettingKeyTypes.Add(new ContributedType(declaringType, owner));
            }
        }
    }
}
