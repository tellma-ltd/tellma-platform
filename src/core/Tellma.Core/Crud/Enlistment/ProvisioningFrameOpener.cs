// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Crud.Save;
using Tellma.Core.Provisioning;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3 and spec 0011 §6.2: a provisioning step runs in an open frame of its own, under
    // the step's system scope, which behaves as an [ApiAction] host: the step enlists writes with the
    // IOpenWriteHost it injects and persists them itself; a step that enlists nothing persists through
    // its own operations and batches as before.
    /// <summary>Runs each provisioning step inside the open frame of <c>provisioning:&lt;step&gt;</c>. Singleton.</summary>
    internal sealed class ProvisioningFrameOpener : IProvisioningFrameOpener
    {
        /// <inheritdoc />
        public async Task RunAsync(IServiceProvider scopedServices, string stepName, Type stepType, Func<Task> step, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scopedServices);
            ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
            ArgumentNullException.ThrowIfNull(stepType);
            ArgumentNullException.ThrowIfNull(step);

            // --- The frame over the step's scope, the step its work ---
            var frame = OpenWriteFrame.ForProvisioning(scopedServices.GetRequiredService<SaveOperation>(), stepName, stepType, cancellationToken);
            _ = await frame.RunAsync(async () =>
            {
                await step().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
    }
}
