// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>The documents the request-shape suite submits.</summary>
    /// <remarks>
    ///     A minimal document carries only what the vendor requires, so a snapshot over it says
    ///     exactly which fields reach the wire; a full one carries every optional field there is, so
    ///     a snapshot over it says how each is spelled and formatted.
    /// </remarks>
    internal static class MarminAeSamples
    {
        /// <summary>An invoice with nothing on it the vendor does not require.</summary>
        /// <returns>The invoice.</returns>
        internal static MarminAeSalesInvoiceRequest MinimalInvoice()
        {
            return new MarminAeSalesInvoiceRequest
            {
                IssueDate = new DateOnly(2026, 5, 7),
                DueDate = new DateOnly(2026, 6, 6),
                InvoiceTypeCode = "380",
                ProfileExecutionId = "00000000",
                DocumentCurrencyCode = "AED",
                AccountingCustomerParty = MinimalParty(),
                DocumentLines = [MinimalLine()],
            };
        }

        /// <summary>A credit note with nothing on it the vendor does not require.</summary>
        /// <returns>The credit note.</returns>
        internal static MarminAeSalesCreditNoteRequest MinimalCreditNote()
        {
            return new MarminAeSalesCreditNoteRequest
            {
                IssueDate = new DateOnly(2026, 5, 7),
                CreditNoteTypeCode = "381",
                DiscrepancyResponse = "DL8.61.1.A",
                ProfileExecutionId = "00000000",
                DocumentCurrencyCode = "AED",
                AccountingCustomerParty = MinimalParty(),
                DocumentLines = [MinimalLine()],
                BillingReference = [new MarminAeDocumentReference { Id = "INV-001", IssueDate = new DateOnly(2026, 4, 30) }],
            };
        }

        /// <summary>An invoice carrying every optional field the vendor documents.</summary>
        /// <returns>The invoice.</returns>
        internal static MarminAeSalesInvoiceRequest FullInvoice()
        {
            return new MarminAeSalesInvoiceRequest
            {
                IssueDate = new DateOnly(2026, 5, 7),
                IssueTime = new TimeOnly(10, 30, 0),
                DueDate = new DateOnly(2026, 6, 6),
                InvoiceTypeCode = "380",
                ProfileExecutionId = "10000001",
                DocumentNumber = "INV-2026-0042",
                DocumentCurrencyCode = "USD",
                TaxCurrencyCode = "AED",
                TaxPointDate = new DateOnly(2026, 5, 6),
                TaxExchangeRate = new MarminAeTaxExchangeRate
                {
                    SourceCurrencyCode = "USD",
                    TargetCurrencyCode = "AED",
                    CalculationRate = 3.6725m,
                },
                AccountingCost = "COST-001",
                BuyerReference = "BR-REF-001",
                Note = "Thank you for your business",
                DocumentSource = "Tellma",
                PrepaidAmount = 12.5m,
                PayableRoundingAmount = -0.02m,
                AccountingCustomerParty = FullParty(),
                BuyerCustomerParty = new MarminAePartyReference { Id = "1001234567" },
                SellerSupplierParty = new MarminAePartyReference { Id = "100123456789003" },
                PayeeParty = new MarminAePayeeParty
                {
                    PartyName = "Payee Ltd",
                    PartyIdentification = new MarminAePartyReference { Id = "PAYEE-1" },
                    FinancialAccount = new MarminAePayeeFinancialAccount
                    {
                        Id = "AE070331234567890123456",
                        Name = "Payee Ltd",
                        FinancialInstitutionBranchId = "BRANCH-1",
                        Address = Address(),
                    },
                },
                TaxRepresentativeParty = new MarminAeTaxRepresentativeParty
                {
                    PartyName = "Representative Ltd",
                    PartyIdentification = new MarminAePartyReference { Id = "REP-1" },
                    PostalAddress = Address(),
                },
                InvoicePeriod = new MarminAeInvoicePeriod
                {
                    StartDate = new DateOnly(2026, 5, 1),
                    EndDate = new DateOnly(2026, 5, 31),
                    Description = "MTH",
                },
                OrderReference = new MarminAeOrderReference
                {
                    Id = "ORD-001",
                    IssueDate = new DateOnly(2026, 4, 20),
                    SalesOrderId = "SO-001",
                },
                BillingReference = [new MarminAeDocumentReference { Id = "INV-PRE-001", IssueDate = new DateOnly(2026, 4, 15) }],
                DespatchDocumentReference = new MarminAeDocumentReference { Id = "DESP-001" },
                ReceiptDocumentReference = new MarminAeDocumentReference { Id = "REC-001" },
                StatementDocumentReference = new MarminAeDocumentReference { Id = "STMT-001" },
                OriginatorDocumentReference = new MarminAeDocumentReference { Id = "ORIG-001" },
                ContractDocumentReference = new MarminAeDocumentReference { Id = "CONTRACT-001" },
                ProjectReference = new MarminAeProjectReference { Id = "PRJ-001", Name = "Project One" },
                Delivery = new MarminAeDelivery
                {
                    ActualDeliveryDate = new DateOnly(2026, 5, 8),
                    PartyName = "Consignee Ltd",
                    PartyId = "CONSIGNEE-1",
                    Terms = "DDP",
                    DeliveryLocation = new MarminAeDeliveryLocation { Id = "LOC-001", Address = Address() },
                },
                PaymentMeans =
                [
                    new MarminAePaymentMeans
                    {
                        PaymentMeansCode = "30",
                        Id = "PM-1",
                        PaymentId = ["PAY-REF-001"],
                        PayeeFinancialAccount = new MarminAePayeeFinancialAccount
                        {
                            Id = "AE070331234567890123456",
                            Name = "Account Name",
                        },
                    },
                    new MarminAePaymentMeans
                    {
                        PaymentMeansCode = "54",
                        CardAccount = new MarminAeCardAccount
                        {
                            PrimaryAccountNumberId = "411111******1111",
                            NetworkId = "VISA",
                            HolderName = "A Buyer",
                        },
                    },
                    new MarminAePaymentMeans
                    {
                        PaymentMeansCode = "49",
                        PaymentMandate = new MarminAePaymentMandate
                        {
                            Id = "MANDATE-1",
                            PayerFinancialAccountId = "AE070331234567890123999",
                        },
                    },
                ],
                PaymentTerms = new MarminAePaymentTerms
                {
                    Note = "Net 30",
                    DueDate = new DateOnly(2026, 6, 6),
                    InstallmentDueDate = new DateOnly(2026, 5, 20),
                    Amount = 100m,
                    PaymentMeansId = "PM-1",
                },
                Charges =
                [
                    new MarminAeCharge
                    {
                        ReasonCode = "FC",
                        Reason = "Freight charge",
                        Amount = 50m,
                        MultiplierFactorNumeric = 5m,
                        BaseAmount = 1000m,
                        TaxCategory = StandardTaxCategory(),
                    },
                ],
                Allowances =
                [
                    new MarminAeAllowance
                    {
                        ReasonCode = "95",
                        Reason = "Discount",
                        Amount = 10m,
                        TaxCategory = StandardTaxCategory(),
                    },
                ],
                Attachments =
                [
                    new MarminAeAttachmentRequest
                    {
                        FileName = "delivery-note.pdf",
                        FileType = "application/pdf",
                        FileContent = "SGVsbG8=",
                    },
                ],
                DocumentLines = [FullLine()],
            };
        }

        /// <summary>The party a minimal document is issued to.</summary>
        /// <returns>The party.</returns>
        internal static MarminAePartyRequest MinimalParty()
        {
            return new MarminAePartyRequest
            {
                Name = "Buyer Business LLC",
                Email = "buyer@example.com",
                EndpointId = "1000000001",
                EndpointSchemeId = "0235",
                PostalAddress = Address(),
            };
        }

        /// <summary>A party carrying every optional field.</summary>
        /// <returns>The party.</returns>
        internal static MarminAePartyRequest FullParty()
        {
            return new MarminAePartyRequest
            {
                Name = "Buyer Business LLC",
                PartyName = "Buyer Business Limited Liability Company",
                Email = "buyer@example.com",
                Telephone = "97141234567",
                EndpointId = "1000000001",
                EndpointSchemeId = "0235",
                Tin = "1000000001",
                SchemeAgencyId = "TL",
                CompanyId = "1234567111",
                AuthorityName = "Dubai Department of Economy and Tourism",
                PassportIssuingCountryCode = "AE",
                PartyTaxScheme = new MarminAePartyTaxScheme
                {
                    CompanyId = "100000000000003",
                    TaxScheme = "VAT",
                },
                PostalAddress = Address(),
            };
        }

        /// <summary>The address every sample party sits at.</summary>
        /// <returns>The address.</returns>
        internal static MarminAeAddress Address()
        {
            return new MarminAeAddress
            {
                StreetName = "Al Asayel St",
                AdditionalStreetName = "Internet City",
                CityName = "Dubai",
                PostalZone = "00000",
                CountrySubentity = "DXB",
                AddressLine = "Building 3",
                Country = "United Arab Emirates",
                CountryCode = "AE",
            };
        }

        /// <summary>The standard-rate VAT treatment the samples use.</summary>
        /// <returns>The tax category.</returns>
        internal static MarminAeTaxCategory StandardTaxCategory()
        {
            return new MarminAeTaxCategory { Id = "S", Percent = 5m, TaxScheme = "VAT" };
        }

        /// <summary>A line with nothing on it the vendor does not require.</summary>
        /// <returns>The line.</returns>
        internal static MarminAeDocumentLineRequest MinimalLine()
        {
            return new MarminAeDocumentLineRequest
            {
                Name = "Product A",
                Description = "Product A description",
                Quantity = 2m,
                UnitCode = "EA",
                Price = new MarminAePriceRequest { BaseAmount = 100m, BaseQuantity = 1m },
                ClassifiedTaxCategory = StandardTaxCategory(),
            };
        }

        /// <summary>A line carrying every optional field.</summary>
        /// <returns>The line.</returns>
        internal static MarminAeDocumentLineRequest FullLine()
        {
            return new MarminAeDocumentLineRequest
            {
                Name = "Product A",
                Description = "Product A description",
                Quantity = 2.5m,
                UnitCode = "EA",
                Note = "Line note",
                AccountingCost = "LINE-COST",
                LotNumberId = "LOT-1",
                LineObjectIdentifier = "LINE-OBJ-1",
                Price = new MarminAePriceRequest
                {
                    BaseAmount = 100.125m,
                    BaseQuantity = 1m,
                    Allowance = new MarminAePriceAllowance { Amount = 2m, BaseAmount = 102.125m },
                },
                ClassifiedTaxCategory = StandardTaxCategory(),
                InvoicePeriod = new MarminAeInvoicePeriod
                {
                    StartDate = new DateOnly(2026, 5, 1),
                    EndDate = new DateOnly(2026, 5, 31),
                },
                OrderLineReference = new MarminAeOrderLineReference { LineId = "LINE-001", OrderReferenceId = "ORD-001" },
                DespatchLineReference = new MarminAeDespatchLineReference { LineId = "DESP-LINE-1" },
                DocumentReference = new MarminAeDocumentReference { Id = "DOC-1", IssueDate = new DateOnly(2026, 4, 1) },
                Charges = [new MarminAeCharge { ReasonCode = "FC", Amount = 1m }],
                Allowances = [new MarminAeAllowance { ReasonCode = "95", Amount = 0.5m }],
                BuyerItemIdentification = new MarminAeItemIdentification { Id = "BUYER-ITEM-1" },
                SellerItemIdentification = new MarminAeItemIdentification { Id = "SELLER-ITEM-1" },
                StandardItemIdentification = new MarminAeStandardItemIdentification { Id = "05412345678908", SchemeId = "0160" },
                AdditionalItemIdentification =
                [
                    new MarminAeAdditionalItemIdentification { Id = "998313", SchemeId = "SAC", SchemeVersionId = "1" },
                ],
                OriginCountry = new MarminAeOriginCountry { IdentificationCode = "AE" },
                CommodityClassification = new MarminAeCommodityClassification
                {
                    CommodityCode = "B",
                    ItemClassificationCode = "847130",
                    ItemClassificationListId = "HS",
                    NatureCode = "RCM-1",
                },
                AdditionalItemProperty =
                [
                    new MarminAeAdditionalItemProperty { Name = "Colour", Value = "Black" },
                ],
            };
        }
    }
}
