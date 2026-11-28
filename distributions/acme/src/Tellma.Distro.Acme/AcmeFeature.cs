// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Distro.Acme.Development;
using Tellma.Distro.Acme.Entities;
using Tellma.Distro.Acme.Services;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme
{
    // Spec 0018 §1.4. Acme writes no model configuration and registers no securable by hand: the
    // entities' annotations produce their tables, and the stacks' descriptors their securables and
    // routes. The reports, the report seed and the assignment notification join with specs 0033 and
    // 0028.
    /// <summary>
    ///     Acme's own feature, named <c>acme</c>: the feature its client app's declarations belong to.
    ///     It contributes the currencies, customers and sales invoices with their actions and the
    ///     invoices' related-documents extra, Acme's settings, and the development tenants' sample
    ///     data, which replaces GL's default center tree with Acme's own.
    /// </summary>
    /// <remarks>
    ///     It requires GL, whose centers the invoice lines are analysed by; the composition substitutes
    ///     <see cref="MyCenter" /> for GL's center.
    /// </remarks>
    [Requires(GlModule.FeatureName)]
    public sealed class AcmeFeature : ITellmaFeature
    {
        /// <summary>The feature's name, which the client's feature key repeats.</summary>
        public const string FeatureName = "acme";

        /// <summary>The database schema of Acme's tables, and the first segment of its entities' query names.</summary>
        public const string Schema = "acme";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            // Nothing beyond the attribute's requirement on GL.
            ArgumentNullException.ThrowIfNull(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            // --- The stacks, the invoices' extra and the settings ---
            _ = contribution
                .Entity<Currency>()
                .Entity<Customer, CustomerService>()
                .Entity<SalesInvoice, SalesInvoiceService>()
                .DetailsContributor<SalesInvoice, RelatedDocumentsContributor>()
                .SettingKeys(typeof(AcmeSettings));

            // --- Development data: Acme's center tree in place of GL's default, then the step after GL's ---
            _ = contribution
                .Singleton<IGlSampleCenters, AcmeSampleCenters>()
                .ProvisioningStep<AcmeSampleDataStep>();
        }
    }
}
