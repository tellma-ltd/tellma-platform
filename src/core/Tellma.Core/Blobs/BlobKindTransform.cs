// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>One transform of a blob kind's policy, with who declared it, for the startup gate's messages.</summary>
    /// <param name="Kind">The kind it transforms.</param>
    /// <param name="Configure">The transform.</param>
    /// <param name="Owner">Who declared it: <see cref="HostOwner" />, or a feature's description.</param>
    internal sealed record BlobKindTransform(string Kind, Func<BlobKindPolicy, BlobKindPolicy> Configure, string Owner)
    {
        /// <summary>The owner of a transform the host declared through <see cref="BlobsBuilder.Kind" />.</summary>
        public const string HostOwner = "the host";

        /// <summary>The owner description of a feature's transform.</summary>
        /// <param name="feature">The feature's name.</param>
        /// <returns>The description.</returns>
        public static string FeatureOwner(string feature)
        {
            return $"feature '{feature}'";
        }

        /// <summary>
        ///     Applies every transform of <paramref name="kind" />, in order, each to the result of
        ///     the one before.
        /// </summary>
        /// <param name="kind">The kind.</param>
        /// <param name="policy">The policy the kind's attribute produced.</param>
        /// <param name="transforms">Every transform of the composition, the features' then the host's.</param>
        /// <param name="failure">Why a transform failed: it threw or returned null; null when every one applied.</param>
        /// <returns>The final policy, or null when a transform failed.</returns>
        public static BlobKindPolicy? Apply(
            string kind, BlobKindPolicy policy, IReadOnlyList<BlobKindTransform> transforms, out string? failure)
        {
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(transforms);

            failure = null;
            BlobKindPolicy current = policy;
            foreach (BlobKindTransform transform in transforms)
            {
                if (!string.Equals(transform.Kind, kind, StringComparison.Ordinal))
                {
                    continue;
                }

                // A transform is host or pack code run at startup: whatever it throws is reported as
                // the kind's problem, so the gate names the transform rather than crashing.
                BlobKindPolicy? next;
                try
                {
                    next = transform.Configure(current);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = $"the transform from {transform.Owner} threw {exception.GetType().Name}: {exception.Message}";
                    return null;
                }

                if (next is null)
                {
                    failure = $"the transform from {transform.Owner} returned no policy";
                    return null;
                }

                current = next;
            }

            return current;
        }
    }
}
