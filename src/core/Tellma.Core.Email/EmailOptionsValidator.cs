// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The startup gate on the email composition. Runs under <c>ValidateOnStart</c>, before the
    ///     host serves traffic, and aggregates every problem into one diagnostic so a misconfigured
    ///     deployment is fixed in one restart rather than one problem per restart.
    /// </summary>
    /// <param name="environment">The host environment, which decides the Development defaults and
    ///     gates the log sink.</param>
    /// <param name="registrations">Every transport the composition declared.</param>
    /// <param name="services">The root provider, used only to ask whether the seams the pipeline
    ///     depends on were registered.</param>
    internal sealed class EmailOptionsValidator(
        IHostEnvironment environment,
        IEnumerable<EmailTransportRegistration> registrations,
        IServiceProvider services) : IValidateOptions<EmailOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, EmailOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<EmailTransportRegistration> declared = [.. registrations];
            List<string> failures = [.. EmailTransportResolution.Resolve(options, environment, declared).Failures];

            // The correlation wire envelope and the cross-deployment event filter both key on the
            // deployment id, so a composition without one would stamp mail it cannot recognize later.
            if (services.GetService<DeploymentIdentity>() is null)
            {
                failures.Add(
                    $"No {nameof(DeploymentIdentity)} is registered. Add one at composition, e.g. services.AddSingleton(new {nameof(DeploymentIdentity)}(\"<application>\", builder.Environment.EnvironmentName)).");
            }

            // Asked rather than resolved: a distribution's implementation is scoped over the tenant
            // context and would throw outside a request, so presence is the only safe question here.
            IServiceProviderIsService? isService = services.GetService<IServiceProviderIsService>();
            if (isService is not null && !isService.IsService(typeof(ISandboxContext)))
            {
                failures.Add(
                    $"No {nameof(ISandboxContext)} is registered, so the pipeline cannot tell sandbox tenants from live ones. Distributions register their own; hosts without tenants register {nameof(SandboxContext)}.{nameof(SandboxContext.Never)}.");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
