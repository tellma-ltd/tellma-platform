// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Distro.Acme.Entities;

namespace Tellma.Distro.Acme.Development
{
    // Spec 0018 §6.3's sample rows, as data the step writes and the tests count. The customers are
    // twelve trade names crossed with five lines of business, drawn so that consecutive codes differ
    // in both (12 and 5 share no factor, so position modulo each picks every pair once); the
    // invoices cycle over the first eight customers, so each has two or three invoices and the
    // related-documents extra has rows to show.
    /// <summary>
    ///     The sample rows Acme's development tenants hold: two currencies, sixty customers of all
    ///     three types, the <c>Sales</c> and center-reader roles, and twenty sales invoices over the
    ///     sample centers.
    /// </summary>
    public static class AcmeSampleData
    {
        /// <summary>The code of the role the sales principals belong to.</summary>
        public const string SalesRoleCode = "Sales";

        /// <summary>The code of the role whose one grant is a filtered read of the centers.</summary>
        public const string CenterReadersRoleCode = "CenterReaders";

        /// <summary>The center readers' filter on <c>gl.Center</c>: the sales centers alone.</summary>
        public const string CenterReadersFilter = "CenterType = 'Sale'";

        /// <summary>The number of sample customers.</summary>
        public const int CustomerCount = 60;

        /// <summary>The number of sample invoices, numbered from 1.</summary>
        public const int InvoiceCount = 20;

        /// <summary>The local currency's code.</summary>
        public const string LocalCurrency = "SAR";

        /// <summary>The foreign currency's code.</summary>
        public const string ForeignCurrency = "USD";

        /// <summary>The number of invoices that cycle over each customer in turn.</summary>
        private const int InvoicedCustomers = 8;

        /// <summary>The days between consecutive invoices' posting dates.</summary>
        private const int DaysBetweenInvoices = 3;

        /// <summary>The trade names, each in English, then Arabic.</summary>
        private static readonly (string English, string Arabic)[] TradeNames =
        [
            ("Al Noor", "النور"), ("Desert Rose", "وردة الصحراء"), ("Falcon", "الصقر"), ("Oasis", "الواحة"),
            ("Palm Grove", "بستان النخيل"), ("Red Sea", "البحر الأحمر"), ("Najd", "نجد"), ("Gulf Star", "نجمة الخليج"),
            ("Horizon", "الأفق"), ("Crescent", "الهلال"), ("Summit", "القمة"), ("Cedar", "الأرز"),
        ];

        /// <summary>The lines of business: each name's English and Arabic patterns, its type and its credit limit's base.</summary>
        private static readonly (string English, string Arabic, CustomerType Type, decimal Limit)[] Businesses =
        [
            ("{0} Trading", "{0} للتجارة", CustomerType.Corporate, 250_000m),
            ("{0} Logistics", "{0} للخدمات اللوجستية", CustomerType.Corporate, 150_000m),
            ("{0} Foods", "{0} للأغذية", CustomerType.Retail, 50_000m),
            ("{0} Pharmacy", "صيدلية {0}", CustomerType.Retail, 25_000m),
            ("{0} Authority", "هيئة {0}", CustomerType.Government, 1_000_000m),
        ];

        /// <summary>The contacts' given names.</summary>
        private static readonly string[] ContactNames =
            ["Sara Al-Qahtani", "Omar Haddad", "Layla Mansour", "Khalid Al-Harbi", "Noura Saleh", "Faisal Al-Otaibi", "Huda Karim", "Yousef Nasser"];

        /// <summary>The descriptions the invoice lines take in turn.</summary>
        private static readonly string[] LineDescriptions =
            ["Office supplies", "Delivery", "Installation", "Maintenance", "Consulting", "Training"];

        /// <summary>The positions of the four customers the sample data deactivates, none of them invoiced.</summary>
        private static readonly int[] DeactivatedPositions = [11, 23, 37, 52];

        /// <summary>The currencies.</summary>
        public static IReadOnlyList<SampleCurrency> Currencies { get; } =
        [
            new(LocalCurrency, "Saudi Riyal", "ريال سعودي"),
            new(ForeignCurrency, "US Dollar", "دولار أمريكي"),
        ];

        /// <summary>The customers, in code order.</summary>
        public static IReadOnlyList<SampleCustomer> Customers { get; } = BuildCustomers();

        /// <summary>The codes of the customers the sample data deactivates.</summary>
        public static IReadOnlyList<string> DeactivatedCustomers { get; } = [.. DeactivatedPositions.Select(CustomerCode)];

        /// <summary>The invoices, in serial order.</summary>
        public static IReadOnlyList<SampleInvoice> Invoices { get; } = BuildInvoices();

        /// <summary>The serial numbers of the invoices the sample data closes: the six oldest.</summary>
        public static IReadOnlyList<int> ClosedInvoices { get; } = [1, 2, 3, 4, 5, 6];

        /// <summary>The code of the customer at a position: <c>C-001</c> for the first.</summary>
        /// <param name="position">The position, from 0.</param>
        /// <returns>The code.</returns>
        private static string CustomerCode(int position)
        {
            return string.Create(CultureInfo.InvariantCulture, $"C-{position + 1:000}");
        }

        /// <summary>Builds the customers.</summary>
        /// <returns>The customers.</returns>
        private static SampleCustomer[] BuildCustomers()
        {
            var customers = new SampleCustomer[CustomerCount];
            for (int position = 0; position < CustomerCount; position++)
            {
                (string tradeEnglish, string tradeArabic) = TradeNames[position % TradeNames.Length];
                (string english, string arabic, CustomerType type, decimal limit) = Businesses[position % Businesses.Length];

                // --- Every fourth customer trades in dollars; every sixth has no credit; every sixth has a picture ---
                string currency = position % 4 == 3 ? ForeignCurrency : LocalCurrency;
                decimal creditLimit = position % 6 == 5 ? 0m : limit * ((position % 4) + 1);
                int? image = position % 6 == 0 ? position / 6 : null;

                // --- One contact for every third customer, a second for every ninth ---
                List<SampleContact> contacts = [];
                for (int contact = 0; contact < (position % 9 == 0 ? 2 : position % 3 == 0 ? 1 : 0); contact++)
                {
                    int number = (position * 2) + contact;
                    contacts.Add(new SampleContact(
                        ContactNames[number % ContactNames.Length],
                        string.Create(CultureInfo.InvariantCulture, $"contact{number + 1}@example.com"),
                        string.Create(CultureInfo.InvariantCulture, $"+96650{1_000_000 + (number * 7919):0000000}")));
                }

                customers[position] = new SampleCustomer(
                    CustomerCode(position),
                    string.Format(CultureInfo.InvariantCulture, english, tradeEnglish),
                    string.Format(CultureInfo.InvariantCulture, arabic, tradeArabic),
                    type,
                    currency,
                    creditLimit,
                    image,
                    contacts);
            }

            return customers;
        }

        /// <summary>Builds the invoices.</summary>
        /// <returns>The invoices.</returns>
        private static SampleInvoice[] BuildInvoices()
        {
            var invoices = new SampleInvoice[InvoiceCount];
            for (int serial = 1; serial <= InvoiceCount; serial++)
            {
                // --- One to three lines, each over another invoiced sales center ---
                List<SampleInvoiceLine> lines = [];
                for (int line = 0; line <= serial % 3; line++)
                {
                    int hundreds = ((serial * 37) + (line * 113)) % 50;
                    lines.Add(new SampleInvoiceLine(
                        AcmeSampleCenters.InvoicedSales[(serial + line) % AcmeSampleCenters.InvoicedSales.Count],
                        LineDescriptions[(serial + line) % LineDescriptions.Length],
                        1_000m + (hundreds * 100m)));
                }

                // The newest invoice is dated today, each older one three days before the next.
                invoices[serial - 1] = new SampleInvoice(
                    serial,
                    (InvoiceCount - serial) * DaysBetweenInvoices,
                    CustomerCode((serial - 1) % InvoicedCustomers),
                    lines);
            }

            return invoices;
        }
    }

    /// <summary>A sample currency.</summary>
    /// <param name="Code">The ISO 4217 code.</param>
    /// <param name="English">The name in English.</param>
    /// <param name="Arabic">The name in Arabic.</param>
    public sealed record SampleCurrency(string Code, string English, string Arabic);

    /// <summary>A sample customer.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="English">The name in English.</param>
    /// <param name="Arabic">The name in Arabic.</param>
    /// <param name="Type">The type.</param>
    /// <param name="CurrencyCode">The code of the currency the customer trades in.</param>
    /// <param name="CreditLimit">The credit limit.</param>
    /// <param name="ImageNumber">The number of the drawn picture the customer shows; null for none.</param>
    /// <param name="Contacts">The contacts.</param>
    public sealed record SampleCustomer(
        string Code, string English, string Arabic, CustomerType Type, string CurrencyCode, decimal CreditLimit, int? ImageNumber, IReadOnlyList<SampleContact> Contacts);

    /// <summary>A contact of a sample customer.</summary>
    /// <param name="Name">The name.</param>
    /// <param name="Email">The email address, at the reserved example domain.</param>
    /// <param name="Mobile">The mobile number.</param>
    public sealed record SampleContact(string Name, string Email, string Mobile);

    /// <summary>A sample sales invoice.</summary>
    /// <param name="SerialNumber">The serial number.</param>
    /// <param name="DaysAgo">The days between the posting date and the day the data is written.</param>
    /// <param name="CustomerCode">The customer's code.</param>
    /// <param name="Lines">The lines.</param>
    public sealed record SampleInvoice(int SerialNumber, int DaysAgo, string CustomerCode, IReadOnlyList<SampleInvoiceLine> Lines);

    /// <summary>A line of a sample sales invoice.</summary>
    /// <param name="CenterCode">The code of the sales center the line is analysed by.</param>
    /// <param name="Description">The description.</param>
    /// <param name="Amount">The amount.</param>
    public sealed record SampleInvoiceLine(string CenterCode, string Description, decimal Amount);
}
