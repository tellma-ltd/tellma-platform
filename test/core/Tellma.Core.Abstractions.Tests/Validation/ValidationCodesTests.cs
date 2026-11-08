// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tests.Api;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Validation
{
    /// <summary>Codes are resource keys clients render from, so a code is never renamed: each is pinned to its text.</summary>
    public class ValidationCodesTests
    {
        [Fact]
        public void The_platform_validation_codes_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(ValidationCodes.Required)] = "Required",
                    [nameof(ValidationCodes.MaxLength)] = "MaxLength",
                    [nameof(ValidationCodes.Range)] = "Range",
                    [nameof(ValidationCodes.Unique)] = "Unique",
                    [nameof(ValidationCodes.WriteOnce)] = "WriteOnce",
                    [nameof(ValidationCodes.Precision)] = "Precision",
                    [nameof(ValidationCodes.ValueNotInSet)] = "Validation.ValueNotInSet",
                    [nameof(ValidationCodes.EntityNotFound)] = "Entity.NotFound",
                    [nameof(ValidationCodes.EntityDuplicateId)] = "Entity.DuplicateId",
                    [nameof(ValidationCodes.FkNotFound)] = "Fk.NotFound",
                    [nameof(ValidationCodes.FkInUse)] = "Fk.InUse",
                    [nameof(ValidationCodes.TreeCycle)] = "Tree.Cycle",
                    [nameof(ValidationCodes.TreeTooDeep)] = "Tree.TooDeep",
                    [nameof(ValidationCodes.BlobNotAttachable)] = "Blob.NotAttachable",
                    [nameof(ValidationCodes.BlobDuplicateReference)] = "Blob.DuplicateReference",
                    [nameof(ValidationCodes.ConcurrencyStampRequired)] = "Concurrency.StampRequired",
                    [nameof(ValidationCodes.ImportLanguageNotConfigured)] = "Import.LanguageNotConfigured",
                    [nameof(ValidationCodes.QueryGeographyNotSelectable)] = "Query.GeographyNotSelectable",
                    [nameof(ValidationCodes.QueryPathNotReachable)] = "Query.PathNotReachable",
                    [nameof(ValidationCodes.QueryUnknownNamedFilter)] = "Query.UnknownNamedFilter",
                    [nameof(ValidationCodes.QueryArithmeticFailed)] = "Query.ArithmeticFailed",
                    [nameof(ValidationCodes.DisplayNamePrefixSeparator)] = "DisplayName.PrefixSeparator",
                },
                WireNameTests.Constants(typeof(ValidationCodes)));
        }
    }
}
