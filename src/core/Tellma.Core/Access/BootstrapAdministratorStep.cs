// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Access
{
    // The step is spec 0011 §6.2's platform step 10, which calls spec 0014 §11's bootstrapper. Its
    // run on a provisioned tenant is spec 0014 §8.3's recovery path, which spec 0011 §6.1 leaves out.
    /// <summary>
    ///     <c>core.bootstrap-administrator</c>: makes the administrator the run names an individual
    ///     administrator of the tenant, named by the local part of the email address and preferring
    ///     the distribution's default language.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The step runner schedules it by the run's administrator, not by the tenant's record: a
    ///         run that names none, such as a migration, neither runs nor records it, and a run that
    ///         names one runs it whatever the tenant records. A <c>provision</c> that names an
    ///         administrator of an active or read-only tenant therefore bootstraps that administrator,
    ///         which is how a tenant provisioned without one, or left without one, gets one.
    ///     </para>
    ///     <para>
    ///         The bootstrap is idempotent on the email address, so a re-run leaves the tenant as it
    ///         was, and a run naming another administrator adds to the tenant and never removes one.
    ///     </para>
    /// </remarks>
    /// <param name="languages">The language catalogue, whose first offered language is the distribution's default.</param>
    internal sealed class BootstrapAdministratorStep(ILanguageCatalog languages) : ITenantProvisioningStep
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "core.bootstrap-administrator";

        /// <summary>The step's order: the first platform step, before the settings (15).</summary>
        public const int StepOrder = 10;

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => StepOrder;

        /// <inheritdoc />
        public int Version => 1;

        /// <inheritdoc />
        public async Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The runner never runs the step without an administrator; a direct call without one does nothing.
            if (context.AdminEmail is not string email || string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            // --- The administrator as the run names it, through the scope's bootstrapper ---
            ITenantBootstrapper bootstrapper = context.Services.GetRequiredService<ITenantBootstrapper>();
            TenantBootstrapRequest request = new(email, LocalPart(email), languages.Offered[0].Code, context.AdminSubject);
            _ = await bootstrapper.BootstrapAdministratorAsync(request).ConfigureAwait(false);
        }

        /// <summary>The local part of an email address, the name the administrator starts with.</summary>
        /// <param name="email">The address.</param>
        /// <returns>What precedes the last <c>@</c>, trimmed; the whole address when that is empty.</returns>
        internal static string LocalPart(string email)
        {
            ArgumentNullException.ThrowIfNull(email);
            string trimmed = email.Trim();
            int at = trimmed.LastIndexOf('@');
            return at > 0 ? trimmed[..at] : trimmed;
        }
    }
}
