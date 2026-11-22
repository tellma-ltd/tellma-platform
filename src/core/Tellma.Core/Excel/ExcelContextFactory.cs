// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Builds the Excel codec's context from the scope's request context: the tenant, the
    ///     deployment, the unit of work's instant, the tenant's content languages in slot order, and
    ///     the reader's culture, calendar and display zone. Scoped.
    /// </summary>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="deployment">The deployment's identity.</param>
    /// <param name="labels">The label provider.</param>
    /// <param name="catalog">The language catalog.</param>
    internal sealed class ExcelContextFactory(
        IRequestContextAccessor accessor,
        DeploymentIdentity deployment,
        ILabelProvider labels,
        ILanguageCatalog catalog)
    {
        /// <summary>The scope's request context.</summary>
        private readonly IRequestContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

        /// <summary>The deployment's identity.</summary>
        private readonly DeploymentIdentity _deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));

        /// <summary>The label provider.</summary>
        private readonly ILabelProvider _labels = labels ?? throw new ArgumentNullException(nameof(labels));

        /// <summary>The language catalog.</summary>
        private readonly ILanguageCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        /// <summary>The context of the scope's request.</summary>
        /// <returns>The context.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        public ExcelContext Create()
        {
            RequestContext request = _accessor.Current;
            TenantDescriptor tenant = request.RequireTenant();

            // The settings' languages, read by the connect prologue, are the tenant's current ones;
            // the descriptor's stand in before they are read.
            IReadOnlyList<string> languages = request.TenantSettings?.Languages is { Count: > 0 } configured
                ? [.. configured.Select(static language => language.Code)]
                : tenant.Languages;

            return new ExcelContext(
                tenant.Id,
                _deployment.DeploymentId,
                request.Now,
                languages,
                request.CultureInfo,
                request.CalendarSystem,
                request.Localization.TimeZone,
                _labels,
                _catalog);
        }
    }
}
