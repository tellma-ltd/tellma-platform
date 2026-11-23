// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Composition;

namespace Tellma.Core.Sharing
{
    /// <summary>
    ///     Registers what the sharing model needs beyond each shareable declaration: its options,
    ///     bound from <c>Tellma:Sharing</c> and validated by the startup gate, and the check that every
    ///     shareable pair is whole in the built model.
    /// </summary>
    internal static class SharingComposition
    {
        /// <summary>Registers the options, the shareables check and the share entities the client metadata leaves out.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, which the check reads and keeps.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation: a value out of range fails startup ---
            options.Bind<SharingOptions>(SharingOptions.SectionPath, CoreFeature.FeatureName);

            // --- The shareables check, after the built-in checks registered before it ---
            ShareablesCheck check = new(record);
            record.BuiltInChecks.Add(check);
            _ = services.AddSingleton<IStartupCheck>(check);

            // --- The share entities' leaves, which the client entity model leaves out ---
            _ = services.AddSingleton(new ShareEntityTypes(record.Shareables.Select(shareable => record.LeafOf(shareable.ShareType)).ToHashSet()));
        }
    }
}
