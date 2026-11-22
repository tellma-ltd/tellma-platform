// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Module.Gl;

namespace Tellma.Distro.Acme.Development
{
    // Spec 0018 §6.3: a 40-node, four-level tree that replaces GL's eight default rows, large enough
    // to page, to show tiles and search ancestors, and to restore a tree with inactive nodes. Only an
    // abstract center or a business unit has children (spec 0018 §5.4).
    /// <summary>
    ///     The sample centers GL's provisioning step writes into an Acme development tenant: the group
    ///     over its retail, wholesale and projects business units, their regional units and their
    ///     operating and sales centers, and the shared services, named in English and Arabic.
    /// </summary>
    /// <remarks>
    ///     The sample data step deactivates <see cref="Inactive" /> afterwards, and analyses its
    ///     invoices by the active sales centers of <see cref="InvoicedSales" />.
    /// </remarks>
    public sealed class AcmeSampleCenters : IGlSampleCenters
    {
        /// <summary>The number of centers in the tree.</summary>
        public const int Count = 40;

        /// <summary>The codes of the four centers the sample data deactivates: the Dammam stores with their two centers, and security.</summary>
        public static readonly IReadOnlyList<string> Inactive = ["BU-RTL-DMM", "OP-RTL-DMM", "SL-RTL-DMM", "SV-ADM-SEC"];

        /// <summary>The active sales centers the sample invoices' lines are analysed by.</summary>
        public static readonly IReadOnlyList<string> InvoicedSales =
            ["SL-RTL-RUH", "SL-RTL-JED", "SL-RTL-ONL", "SL-WHL-C", "SL-WHL-W", "SL-WHL-E", "SL-PRJ-GOV", "SL-PRJ-COR"];

        /// <summary>The tree, parents first, built once.</summary>
        private static readonly SampleCenter[] Tree =
        [
            // --- Level 1: the group ---
            Center("ACME", "Acme Group", "مجموعة أكمي", CenterType.Abstract, null),

            // --- Retail: three city units with their stores, and online sales ---
            Center("BU-RTL", "Retail", "التجزئة", CenterType.BusinessUnit, "ACME"),
            Center("BU-RTL-RUH", "Riyadh Stores", "متاجر الرياض", CenterType.BusinessUnit, "BU-RTL"),
            Center("OP-RTL-RUH", "Riyadh Store Operations", "عمليات متاجر الرياض", CenterType.Operation, "BU-RTL-RUH"),
            Center("SL-RTL-RUH", "Riyadh Store Sales", "مبيعات متاجر الرياض", CenterType.Sale, "BU-RTL-RUH"),
            Center("BU-RTL-JED", "Jeddah Stores", "متاجر جدة", CenterType.BusinessUnit, "BU-RTL"),
            Center("OP-RTL-JED", "Jeddah Store Operations", "عمليات متاجر جدة", CenterType.Operation, "BU-RTL-JED"),
            Center("SL-RTL-JED", "Jeddah Store Sales", "مبيعات متاجر جدة", CenterType.Sale, "BU-RTL-JED"),
            Center("BU-RTL-DMM", "Dammam Stores", "متاجر الدمام", CenterType.BusinessUnit, "BU-RTL"),
            Center("OP-RTL-DMM", "Dammam Store Operations", "عمليات متاجر الدمام", CenterType.Operation, "BU-RTL-DMM"),
            Center("SL-RTL-DMM", "Dammam Store Sales", "مبيعات متاجر الدمام", CenterType.Sale, "BU-RTL-DMM"),
            Center("SL-RTL-ONL", "Online Sales", "المبيعات الإلكترونية", CenterType.Sale, "BU-RTL"),

            // --- Wholesale: three regions with their warehouses and sales, and export ---
            Center("BU-WHL", "Wholesale", "الجملة", CenterType.BusinessUnit, "ACME"),
            Center("BU-WHL-C", "Central Region", "المنطقة الوسطى", CenterType.BusinessUnit, "BU-WHL"),
            Center("OP-WHL-C", "Central Warehouse", "مستودع المنطقة الوسطى", CenterType.Operation, "BU-WHL-C"),
            Center("SL-WHL-C", "Central Wholesale Sales", "مبيعات الجملة في المنطقة الوسطى", CenterType.Sale, "BU-WHL-C"),
            Center("BU-WHL-W", "Western Region", "المنطقة الغربية", CenterType.BusinessUnit, "BU-WHL"),
            Center("OP-WHL-W", "Western Warehouse", "مستودع المنطقة الغربية", CenterType.Operation, "BU-WHL-W"),
            Center("SL-WHL-W", "Western Wholesale Sales", "مبيعات الجملة في المنطقة الغربية", CenterType.Sale, "BU-WHL-W"),
            Center("BU-WHL-E", "Eastern Region", "المنطقة الشرقية", CenterType.BusinessUnit, "BU-WHL"),
            Center("OP-WHL-E", "Eastern Warehouse", "مستودع المنطقة الشرقية", CenterType.Operation, "BU-WHL-E"),
            Center("SL-WHL-E", "Eastern Wholesale Sales", "مبيعات الجملة في المنطقة الشرقية", CenterType.Sale, "BU-WHL-E"),
            Center("SL-WHL-EXP", "Export Sales", "مبيعات التصدير", CenterType.Sale, "BU-WHL"),

            // --- Projects: government and corporate, each with its delivery and its sales ---
            Center("BU-PRJ", "Projects", "المشاريع", CenterType.BusinessUnit, "ACME"),
            Center("BU-PRJ-GOV", "Government Projects", "المشاريع الحكومية", CenterType.BusinessUnit, "BU-PRJ"),
            Center("OP-PRJ-GOV", "Government Project Delivery", "تنفيذ المشاريع الحكومية", CenterType.Operation, "BU-PRJ-GOV"),
            Center("SL-PRJ-GOV", "Government Tenders", "المناقصات الحكومية", CenterType.Sale, "BU-PRJ-GOV"),
            Center("BU-PRJ-COR", "Corporate Projects", "مشاريع الشركات", CenterType.BusinessUnit, "BU-PRJ"),
            Center("OP-PRJ-COR", "Corporate Project Delivery", "تنفيذ مشاريع الشركات", CenterType.Operation, "BU-PRJ-COR"),
            Center("SL-PRJ-COR", "Corporate Project Sales", "مبيعات مشاريع الشركات", CenterType.Sale, "BU-PRJ-COR"),

            // --- Shared services, and the administration under them ---
            Center("SS", "Shared Services", "الخدمات المشتركة", CenterType.Abstract, "ACME"),
            Center("SV-FIN", "Finance", "المالية", CenterType.Service, "SS"),
            Center("SV-HR", "Human Resources", "الموارد البشرية", CenterType.Service, "SS"),
            Center("SV-IT", "Information Technology", "تقنية المعلومات", CenterType.Service, "SS"),
            Center("SV-MKT", "Marketing", "التسويق", CenterType.Service, "SS"),
            Center("AB-ADM", "Administration", "الإدارة", CenterType.Abstract, "SS"),
            Center("SV-ADM-FAC", "Facilities", "المرافق", CenterType.Service, "AB-ADM"),
            Center("SV-ADM-LEG", "Legal", "الشؤون القانونية", CenterType.Service, "AB-ADM"),
            Center("SV-ADM-PRO", "Procurement", "المشتريات", CenterType.Service, "AB-ADM"),
            Center("SV-ADM-SEC", "Security", "الأمن", CenterType.Service, "AB-ADM"),
        ];

        /// <inheritdoc />
        public int Version => 1;

        /// <inheritdoc />
        public IReadOnlyList<SampleCenter> Centers => Tree;

        /// <summary>One sample center named in English, then Arabic.</summary>
        /// <param name="code">The code.</param>
        /// <param name="english">The English name, the fallback.</param>
        /// <param name="arabic">The Arabic name.</param>
        /// <param name="type">The type.</param>
        /// <param name="parentCode">The parent's code; null for the root.</param>
        /// <returns>The center.</returns>
        private static SampleCenter Center(string code, string english, string arabic, CenterType type, string? parentCode)
        {
            return new SampleCenter(code, [new SampleCenterName("en", english), new SampleCenterName("ar", arabic)], type, parentCode);
        }
    }
}
