// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A confirmation key a feature declares: the key of a warning a caller must confirm before an
    ///     operation proceeds. Declared once in the composition; the startup gate verifies that a
    ///     neutral string resource renders it.
    /// </summary>
    /// <param name="Key">The key, dotted PascalCase like a validation code, such as <c>Inventory.BackdatedRecost</c>.</param>
    public sealed record ConfirmationContributionItem(string Key) : FeatureContributionItem;
}
