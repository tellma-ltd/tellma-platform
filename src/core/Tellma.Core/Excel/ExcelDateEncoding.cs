// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>How a date column's cells carry their values, as the workbook's manifest records it.</summary>
    public enum ExcelDateEncoding
    {
        /// <summary>A serial day number with a date format; Gregorian whatever calendar renders it.</summary>
        Serial,

        /// <summary>Text: a calendar's rendering under the manifest's pattern, or ISO 8601.</summary>
        Text,
    }
}
