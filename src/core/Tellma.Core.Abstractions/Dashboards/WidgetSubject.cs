// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Dashboards
{
    /// <summary>What a widget of a type is about, beyond its settings.</summary>
    public enum WidgetSubject
    {
        /// <summary>The widget has no subject: it names neither a stored nor a coded report.</summary>
        None = 0,

        /// <summary>The widget shows one report: exactly one of a stored report's id and a coded report's key.</summary>
        Report = 1,
    }
}
