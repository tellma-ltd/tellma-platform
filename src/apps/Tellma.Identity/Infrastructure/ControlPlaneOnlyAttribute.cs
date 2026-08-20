// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Tellma.Identity.Options;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Marks a controller as part of the operator (control-plane) surface, which exists only in
    ///     standalone deployments. The <see cref="RemoveControlPlaneControllersConvention" /> strips
    ///     these controllers from the application model in in-proc mode, so their routes are not
    ///     merely unauthorized there — they do not exist.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ControlPlaneOnlyAttribute : Attribute;

    /// <summary>
    ///     Removes every <see cref="ControlPlaneOnlyAttribute" />-marked controller from the
    ///     application model when the engine runs in-proc, enforcing the spec's requirement that the
    ///     operator surface is absent there rather than relying on scope authorization alone.
    /// </summary>
    /// <param name="mode">The deployment mode.</param>
    internal sealed class RemoveControlPlaneControllersConvention(TellmaIdentityDeploymentMode mode) : IApplicationModelConvention
    {
        /// <inheritdoc />
        public void Apply(ApplicationModel application)
        {
            ArgumentNullException.ThrowIfNull(application);

            if (mode != TellmaIdentityDeploymentMode.InProc)
            {
                return;
            }

            for (int i = application.Controllers.Count - 1; i >= 0; i--)
            {
                if (application.Controllers[i].Attributes.OfType<ControlPlaneOnlyAttribute>().Any())
                {
                    application.Controllers.RemoveAt(i);
                }
            }
        }
    }
}
