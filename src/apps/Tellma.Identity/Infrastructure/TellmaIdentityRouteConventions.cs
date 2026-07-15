// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Prefixes every identity Razor Page route with the in-proc path base (for example
    ///     <c>/id/Identity/Account/Login</c>), leaving a host's own pages untouched. Pages are
    ///     matched by the <see cref="TellmaIdentityConstants.AreaName" /> area, which the engine
    ///     owns by convention.
    /// </summary>
    /// <param name="prefix">The route prefix without slashes, for example <c>id</c>.</param>
    internal sealed class TellmaIdentityPageRouteConvention(string prefix) : IPageRouteModelConvention
    {
        /// <inheritdoc />
        public void Apply(PageRouteModel model)
        {
            if (!string.Equals(model.AreaName, TellmaIdentityConstants.AreaName, StringComparison.Ordinal))
            {
                return;
            }

            foreach (SelectorModel selector in model.Selectors)
            {
                if (selector.AttributeRouteModel is { } route)
                {
                    route.Template = AttributeRouteModel.CombineTemplates(prefix, route.Template);
                }
            }
        }
    }

    /// <summary>
    ///     Prefixes every identity MVC controller route with the in-proc path base (for example
    ///     <c>/id/connect/authorize</c>), leaving a host's own controllers untouched. Controllers
    ///     are matched by assembly.
    /// </summary>
    /// <param name="prefix">The route prefix without slashes, for example <c>id</c>.</param>
    internal sealed class TellmaIdentityControllerRouteConvention(string prefix) : IApplicationModelConvention
    {
        /// <inheritdoc />
        public void Apply(ApplicationModel application)
        {
            foreach (ControllerModel controller in application.Controllers)
            {
                if (controller.ControllerType.Assembly != typeof(TellmaIdentityControllerRouteConvention).Assembly)
                {
                    continue;
                }

                foreach (SelectorModel selector in controller.Selectors)
                {
                    AttributeRouteModel prefixRoute = new() { Template = prefix };
                    selector.AttributeRouteModel = selector.AttributeRouteModel is { } route
                        ? AttributeRouteModel.CombineAttributeRouteModel(prefixRoute, route)
                        : prefixRoute;
                }
            }
        }
    }
}
