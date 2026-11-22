// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Abstractions.Settings;
using Tellma.Distro.Acme.Entities;

namespace Tellma.Distro.Acme
{
    // Spec 0018 §1.4's five keys (spec 0013 §5.3), one of each shape the settings pages edit: a
    // client-visible string, a server integer, a reference, an enum array and a secret. Each key's
    // middle segment repeats its category, so no key derives a group of its own.
    /// <summary>Acme's tenant settings, each on the settings tab of its category.</summary>
    public static class AcmeSettings
    {
        /// <summary>The settings tab of invoice numbering.</summary>
        public const string InvoicingCategory = "invoicing";

        /// <summary>The settings tab of selling.</summary>
        public const string SalesCategory = "sales";

        /// <summary>The settings tab of the connections to other systems.</summary>
        public const string IntegrationCategory = "integration";

        /// <summary>The most digits an invoice's serial number is padded to.</summary>
        public const int MaxInvoicePadding = 12;

        /// <summary>The text an invoice's number starts with, such as <c>INV-</c>; visible to the client, which previews the next number.</summary>
        public static readonly SettingKey<string> InvoicePrefix = new(
            "acme.invoicing.prefix", "INV-", category: InvoicingCategory, visibility: SettingVisibility.Client);

        /// <summary>The digits an invoice's serial number is padded to, with leading zeros: from 1 to <see cref="MaxInvoicePadding" />.</summary>
        public static readonly SettingKey<int> InvoicePadding = new(
            "acme.invoicing.padding",
            5,
            category: InvoicingCategory,
            validate: static padding => padding is < 1 or > MaxInvoicePadding ? AcmeValidationCodes.InvoicePaddingOutOfRange : null);

        /// <summary>The currency a new customer is billed in; null for none.</summary>
        public static readonly SettingKey<int?> DefaultCurrencyId = new(
            "acme.sales.defaultCurrencyId", null, category: SalesCategory, references: typeof(Currency));

        /// <summary>The kinds of customer the tenant sells to; every kind by default.</summary>
        public static readonly SettingKey<ImmutableArray<CustomerType>> AllowedCustomerTypes = new(
            "acme.sales.allowedCustomerTypes", [CustomerType.Retail, CustomerType.Corporate, CustomerType.Government], category: SalesCategory);

        /// <summary>The key Acme's calls to the Marmin e-invoicing service authenticate with; a secret no reader sees.</summary>
        public static readonly SettingKey<string?> MarminApiKey = new(
            "acme.integration.marminApiKey", null, category: IntegrationCategory, visibility: SettingVisibility.Secret);
    }
}
