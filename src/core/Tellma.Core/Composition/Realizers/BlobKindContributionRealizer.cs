// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Blobs;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="BlobKindContributionItem" />s as blob-kind transforms, in realisation
    ///     order, each with its feature, so the startup gate can say who transformed a kind it refuses.
    /// </summary>
    internal sealed class BlobKindContributionRealizer : IContributionRealizer<BlobKindContributionItem>
    {
        /// <inheritdoc />
        public void Realize(BlobKindContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses nulls; an item built by hand can still carry them.
            if (item.Kind is null || item.Configure is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a blob-kind transform without a kind or without a transform.",
                    "Contribute the transform with its kind: contribution.BlobKind(\"<kind>\", p => p with { … })."));
                return;
            }

            // Whether the kind is declared, and whether the transform's result is sound, is the
            // startup gate's to judge once the model is built.
            context.Record.BlobKindTransforms.Add(new BlobKindTransform(item.Kind, item.Configure, BlobKindTransform.FeatureOwner(owner.Name)));
        }
    }
}
