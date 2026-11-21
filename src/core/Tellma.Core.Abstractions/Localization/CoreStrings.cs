// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The address of Core's shared resources, for a package that renders one of Core's strings
    ///     (a shared property label, a validation code's message) without referencing Core's runtime:
    ///     <c>IStringLocalizerFactory.Create(CoreStrings.BaseName, CoreStrings.Assembly)</c>.
    /// </summary>
    public static class CoreStrings
    {
        /// <summary>The resources' base name.</summary>
        public const string BaseName = "Tellma.Core.Resources.Strings";

        /// <summary>The simple name of the assembly that ships them.</summary>
        public const string Assembly = "Tellma.Core";
    }
}
