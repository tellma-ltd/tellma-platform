// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     Registers what the email outbox needs beyond <see cref="CoreFeature" />'s contribution: its
    ///     options, bound from <c>Tellma:Outbox</c> and validated by the startup gate; its instruments;
    ///     and the check that a host running the sending jobs has an email sender.
    /// </summary>
    internal static class OutboxComposition
    {
        /// <summary>Registers the options, the instruments and the sender check.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, which the sender check reads and keeps.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation: a value out of range fails startup ---
            options.Bind<OutboxOptions>(OutboxOptions.SectionPath, CoreFeature.FeatureName);

            // --- The instruments, on the host's meter factory ---
            _ = services.AddMetrics();
            services.TryAddSingleton<OutboxMetrics>();

            // --- The sender check, after the built-in checks registered before it ---
            OutboxSenderCheck check = new(record);
            record.BuiltInChecks.Add(check);
            _ = services.AddSingleton<IStartupCheck>(check);
        }
    }
}
