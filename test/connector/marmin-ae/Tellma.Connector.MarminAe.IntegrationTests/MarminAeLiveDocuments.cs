// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>The documents the live suite issues into the sandbox.</summary>
    /// <remarks>
    ///     Deliberately minimal: enough for the vendor's own validator to accept, and no more. Every
    ///     one carries the run's marker in the buyer reference, because the sandbox has no delete
    ///     and these accumulate.
    /// </remarks>
    public static class MarminAeLiveDocuments
    {
        /// <summary>Gulf Standard Time, which is the zone every date and time on a document is in.</summary>
        private static readonly TimeSpan GulfOffset = TimeSpan.FromHours(4);

        /// <summary>Today, where the vendor is.</summary>
        public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(GulfOffset).Date);

        /// <summary>A minimal invoice the sandbox will accept.</summary>
        /// <param name="documentNumber">The number to issue it under.</param>
        /// <returns>The invoice.</returns>
        public static MarminAeSalesInvoiceRequest Invoice(string documentNumber)
        {
            return new MarminAeSalesInvoiceRequest
            {
                DocumentNumber = documentNumber,
                IssueDate = Today,
                IssueTime = new TimeOnly(10, 30, 0),
                DueDate = Today.AddDays(30),
                InvoiceTypeCode = "380",
                ProfileExecutionId = "00000000",
                DocumentCurrencyCode = "AED",
                BuyerReference = MarminAeLiveEnvironment.Marker,
                DocumentSource = "Tellma",
                AccountingCustomerParty = Customer(),
                PaymentMeans =
                [
                    new MarminAePaymentMeans
                    {
                        PaymentMeansCode = "30",
                        PayeeFinancialAccount = new MarminAePayeeFinancialAccount
                        {
                            Id = "AE070331234567890123456",
                            Name = "Tellma Live Suite",
                        },
                    },
                ],
                DocumentLines = [Line()],
            };
        }

        /// <summary>A minimal credit note against an invoice already issued.</summary>
        /// <param name="documentNumber">The number to issue it under.</param>
        /// <param name="creditedDocumentNumber">The invoice being credited.</param>
        /// <returns>The credit note.</returns>
        public static MarminAeSalesCreditNoteRequest CreditNote(
            string documentNumber, string creditedDocumentNumber)
        {
            return new MarminAeSalesCreditNoteRequest
            {
                DocumentNumber = documentNumber,
                IssueDate = Today,
                IssueTime = new TimeOnly(11, 0, 0),
                CreditNoteTypeCode = "381",
                DiscrepancyResponse = "DL8.61.1.A",
                ProfileExecutionId = "00000000",
                DocumentCurrencyCode = "AED",
                BuyerReference = MarminAeLiveEnvironment.Marker,
                DocumentSource = "Tellma",
                Reason = "Live suite adjustment",
                AccountingCustomerParty = Customer(),
                BillingReference =
                [
                    new MarminAeDocumentReference { Id = creditedDocumentNumber, IssueDate = Today },
                ],
                DocumentLines = [Line()],
            };
        }

        /// <summary>An invoice the vendor is certain to refuse, for the sake of its refusal.</summary>
        /// <remarks>
        ///     The scenario flags are the wrong length and the currency is not a currency, so the
        ///     refusal is guaranteed and its shape is what the offline error vectors are cut from.
        /// </remarks>
        /// <returns>The invoice.</returns>
        public static MarminAeSalesInvoiceRequest InvalidInvoice()
        {
            return Invoice("LIVE-INVALID") with
            {
                ProfileExecutionId = "not-eight-binary-digits",
                DocumentCurrencyCode = "NOTACURRENCY",
                InvoiceTypeCode = "999",
            };
        }

        /// <summary>A document number no other run will have used.</summary>
        /// <param name="prefix">What to call it.</param>
        /// <returns>The number.</returns>
        public static string DocumentNumber(string prefix)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}");
        }

        private static MarminAePartyRequest Customer()
        {
            return new MarminAePartyRequest
            {
                Name = "Tellma Live Suite Buyer",
                PartyName = "Tellma Live Suite Buyer LLC",
                Email = "buyer@example.com",
                Telephone = "97141234567",
                Tin = "1000000001",
                EndpointId = "1000000001",
                EndpointSchemeId = "0235",
                PartyTaxScheme = new MarminAePartyTaxScheme
                {
                    CompanyId = "100000000100003",
                    TaxScheme = "VAT",
                },
                PostalAddress = new MarminAeAddress
                {
                    StreetName = "Sheikh Zayed Road",
                    CityName = "Dubai",
                    PostalZone = "00000",
                    CountrySubentity = "DXB",
                    Country = "United Arab Emirates",
                    CountryCode = "AE",
                },
            };
        }

        private static MarminAeDocumentLineRequest Line()
        {
            return new MarminAeDocumentLineRequest
            {
                Name = "Consulting",
                Description = "Integration consulting services",
                Quantity = 2m,
                UnitCode = "EA",
                Price = new MarminAePriceRequest { BaseAmount = 100m, BaseQuantity = 1m },
                ClassifiedTaxCategory = new MarminAeTaxCategory
                {
                    Id = "S",
                    Percent = 5m,
                    TaxScheme = "VAT",
                },
                OrderLineReference = new MarminAeOrderLineReference { LineId = "1" },
                CommodityClassification = new MarminAeCommodityClassification
                {
                    CommodityCode = "S",
                },
                AdditionalItemIdentification =
                [
                    new MarminAeAdditionalItemIdentification { Id = "998313", SchemeId = "SAC" },
                ],
            };
        }
    }
}
