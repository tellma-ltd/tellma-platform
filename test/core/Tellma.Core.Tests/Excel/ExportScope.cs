// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Excel;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     A reader's localization entered over the fixture composition, and the export context the
    ///     planner reads in it: the composition's labels, read in the reader's language for a tenant
    ///     with the given content languages.
    /// </summary>
    internal sealed class ExportScope : IDisposable
    {
        /// <summary>The ambient localization, restored on dispose.</summary>
        private readonly IDisposable _scope;

        /// <summary>Enters the reader's localization.</summary>
        /// <param name="services">The fixture composition's services.</param>
        /// <param name="language">The reader's language.</param>
        /// <param name="tenantLanguages">The tenant's content languages, in slot order.</param>
        public ExportScope(IServiceProvider services, string language, params string[] tenantLanguages)
        {
            ArgumentNullException.ThrowIfNull(services);
            ILanguageCatalog catalog = services.GetRequiredService<ILanguageCatalog>();
            string region = catalog.Formats(language).DefaultRegion;
            GregorianCalendarSystem calendar = new();
            System.Globalization.CultureInfo culture = NegotiatedCultures.Get(language, region);
            _scope = LocalizationScope.Enter(new LocalizationContext(
                culture,
                calendar,
                TimeZoneInfo.Utc,
                Math.Max(1, Array.IndexOf(tenantLanguages, language) + 1),
                [.. tenantLanguages.Select(code => catalog.Find(code)!)],
                language,
                language + "-" + region));

            Context = new ExcelContext(
                TenantId: 7,
                DeploymentId: "deployment-a",
                ExcelTestKit.Now,
                tenantLanguages,
                culture,
                calendar,
                TimeZoneInfo.Utc,
                services.GetRequiredService<ILabelProvider>(),
                catalog);
            Metadata = services.GetRequiredService<IEntityMetadataProvider>();
        }

        /// <summary>The export context in the reader's localization.</summary>
        public ExcelContext Context { get; }

        /// <summary>The fixture model's metadata.</summary>
        public IEntityMetadataProvider Metadata { get; }

        /// <summary>A language's symbol, as the catalogue gives it.</summary>
        /// <param name="code">The language.</param>
        /// <returns>The symbol.</returns>
        public string Symbol(string code)
        {
            return Context.Catalog.Find(code)!.Symbol;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}
