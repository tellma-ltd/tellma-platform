// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    // Spec 0018 §5.6's default rows.
    /// <summary>
    ///     The sample centers GL writes into a development tenant unless a dependent feature replaces
    ///     them: a headquarters over a trading and a manufacturing business unit with their operating
    ///     and sales centers, and two service centers, named in English and Arabic.
    /// </summary>
    public sealed class DefaultGlSampleCenters : IGlSampleCenters
    {
        /// <summary>The tree, built once.</summary>
        private static readonly SampleCenter[] Tree =
        [
            Center("HQ", "Headquarters", "المقر الرئيسي", CenterType.Abstract, null),
            Center("BU-TRD", "Trading", "التجارة", CenterType.BusinessUnit, "HQ"),
            Center("OP-TRD", "Trading Operations", "عمليات التجارة", CenterType.Operation, "BU-TRD"),
            Center("SL-TRD", "Trading Sales", "مبيعات التجارة", CenterType.Sale, "BU-TRD"),
            Center("BU-MFG", "Manufacturing", "التصنيع", CenterType.BusinessUnit, "HQ"),
            Center("OP-MFG", "Production", "الإنتاج", CenterType.Operation, "BU-MFG"),
            Center("SV-IT", "Information Technology", "تقنية المعلومات", CenterType.Service, "HQ"),
            Center("SV-HR", "Human Resources", "الموارد البشرية", CenterType.Service, "HQ"),
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
