// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     Registers what the email outbox needs beyond <see cref="CoreFeature" />'s contribution: its
    ///     options, bound from <c>Tellma:Outbox</c> and validated by the startup gate.
    /// </summary>
    internal static class OutboxComposition
    {
        /// <summary>Registers the options.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation: a value out of range fails startup ---
            options.Bind<OutboxOptions>(OutboxOptions.SectionPath, CoreFeature.FeatureName);
        }
    }
}
