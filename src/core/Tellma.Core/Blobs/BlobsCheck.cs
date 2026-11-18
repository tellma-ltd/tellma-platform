// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     <c>blobs</c>: exactly one blob store is registered; an image processor is registered when a
    ///     kind normalises images, and at most one; every kind transform names a declared kind; every
    ///     kind's final policy is sound and within the global upload ceiling; the blob options are
    ///     positive; the file-system store's root is valid and can publish create-only; and a configured
    ///     scratch directory is absolute.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rules on the <c>[BlobReference]</c> declarations themselves (an <c>int?</c> column of
    ///         a stack's aggregate, a well-formed kind declared once, a policy that can be made) are the
    ///         entity-metadata check's, and are not reported twice. Options that cannot be bound are the
    ///         options check's.
    ///     </para>
    ///     <para>
    ///         It needs infrastructure: probing the file-system root creates the directory and, on
    ///         Unix, hard-links a file under it. A build step without a deployment skips it.
    ///     </para>
    /// </remarks>
    /// <param name="record">The realised composition, whose kind transforms are checked.</param>
    internal sealed class BlobsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Blobs;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            BlobRegistrations registrations = services.GetRequiredService<BlobRegistrations>();
            IBlobKindRegistry kinds = services.GetRequiredService<IBlobKindRegistry>();
            bool metadataBuilt = services.GetRequiredService<EntityMetadataSource>().Build.Provider is not null;

            CheckStore(registrations, problems);
            CheckProcessor(registrations, kinds, problems);
            if (metadataBuilt)
            {
                CheckTransforms(kinds, problems);
            }

            foreach (BlobKindDescriptor kind in kinds.All)
            {
                CheckPolicy(kind, problems);
            }

            CheckOptions(services, kinds, problems);
            CheckFileSystemStore(services, registrations, problems);
            CheckScratch(services.GetRequiredService<ScratchDirectory>(), problems);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Exactly one blob store: none has nowhere to put bytes, two cannot both be the one.</summary>
        /// <param name="registrations">The blob registrations.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckStore(BlobRegistrations registrations, List<CompositionProblem> problems)
        {
            IReadOnlyList<ServiceDescriptor> stores = registrations.Stores;
            if (stores.Count == 0)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    "No blob store is registered.",
                    "Register one blob store: services.AddFileSystemBlobStore() for a single-machine deployment, or the Azure store in a cloud one."));
            }
            else if (stores.Count > 1)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{stores.Count.ToString(CultureInfo.InvariantCulture)} blob stores are registered ({DescribeAll(stores)}); a process runs on exactly one.",
                    "Keep one blob store registration and remove the others."));
            }
        }

        /// <summary>An image processor when any kind normalises images; never more than one.</summary>
        /// <param name="registrations">The blob registrations.</param>
        /// <param name="kinds">The kind registry.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckProcessor(BlobRegistrations registrations, IBlobKindRegistry kinds, List<CompositionProblem> problems)
        {
            IReadOnlyList<ServiceDescriptor> processors = registrations.ImageProcessors;
            if (processors.Count > 1)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{processors.Count.ToString(CultureInfo.InvariantCulture)} image processors are registered ({DescribeAll(processors)}); uploads use exactly one.",
                    "Keep one image processor registration: AddTellmaSkia(), or tellma.Blobs(b => b.ImageProcessor<T>()) for another."));
            }
            else if (processors.Count == 0)
            {
                List<string> images = [.. kinds.All.Where(static kind => kind.Policy.Image is not null).Select(static kind => $"'{kind.Kind}'")];
                if (images.Count > 0)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The blob kinds {string.Join(", ", images)} normalise images, but no image processor is registered.",
                        "Register an image processor: AddTellmaSkia(), or tellma.Blobs(b => b.ImageProcessor<T>()) for another."));
                }
            }
        }

        /// <summary>Every kind transform names a kind some <c>[BlobReference]</c> declares.</summary>
        /// <param name="kinds">The kind registry.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckTransforms(IBlobKindRegistry kinds, List<CompositionProblem> problems)
        {
            foreach (BlobKindTransform transform in record.BlobKindTransforms)
            {
                if (kinds.Find(transform.Kind) is null)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The blob kind '{transform.Kind}' configured by {transform.Owner} is not declared by any [BlobReference] of a stack's aggregate.",
                        $"Name a declared kind, or declare '{transform.Kind}' with [BlobReference] on an int? column of a stack's root or child entity."));
                }
            }
        }

        /// <summary>A kind's final policy, after the transforms, can be enforced.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckPolicy(BlobKindDescriptor kind, List<CompositionProblem> problems)
        {
            BlobKindPolicy policy = kind.Policy;
            string fix = $"Correct the policy of '{kind.Kind}': its [BlobReference] or the transform that configures it.";
            void Report(string problem)
            {
                problems.Add(new CompositionProblem(Name, $"The policy of the blob kind '{kind.Kind}' {problem}.", fix));
            }

            // --- The size and the allowed types ---
            if (policy.MaxSize <= 0)
            {
                Report($"has a maximum size of {policy.MaxSize.ToString(CultureInfo.InvariantCulture)} bytes, which is not positive");
            }

            if (policy.AllowedContentTypes is null || policy.AllowedContentTypes.Count == 0)
            {
                Report("allows no content type");
            }
            else
            {
                foreach (string type in policy.AllowedContentTypes)
                {
                    // The sniffer compares canonical media types ordinally, so a type with a parameter,
                    // spaces or capitals would never match an upload.
                    if (type is null || !string.Equals(BlobContentSniffer.ParseMediaType(type), type, StringComparison.Ordinal))
                    {
                        Report($"allows '{type}', which is not a lower-case media type without parameters, such as 'application/pdf'");
                    }
                }
            }

            // --- The image normalisation ---
            if (policy.Image is ImagePolicy image)
            {
                if (image.MaxDimension <= 0)
                {
                    Report($"normalises images to {image.MaxDimension.ToString(CultureInfo.InvariantCulture)} pixels, which is not positive");
                }

                if (image.MaxInputPixels <= 0)
                {
                    Report($"accepts images of at most {image.MaxInputPixels.ToString(CultureInfo.InvariantCulture)} pixels, which is not positive");
                }

                if (!Enum.IsDefined(image.Fit))
                {
                    Report($"fits images with {image.Fit}, which is not a defined fit");
                }

                if (!Enum.IsDefined(image.Format))
                {
                    Report($"encodes images as {image.Format}, which is not a defined format");
                }
            }

            // --- The thumbnail ---
            if (policy.Thumbnail is ThumbnailPolicy thumbnail)
            {
                if (thumbnail.Size <= 0)
                {
                    Report($"renders thumbnails of {thumbnail.Size.ToString(CultureInfo.InvariantCulture)} pixels, which is not positive");
                }

                if (!Enum.IsDefined(thumbnail.Format))
                {
                    Report($"encodes thumbnails as {thumbnail.Format}, which is not a defined format");
                }
            }

            // --- The staging lifetime and the read access ---
            if (policy.StagingTtl is TimeSpan ttl && !IsSeconds(ttl))
            {
                Report($"stages uploads for {ttl}, which is not a positive number of whole seconds a staged row can hold");
            }

            if (!Enum.IsDefined(policy.ReadAccess))
            {
                Report($"has the read access {policy.ReadAccess}, which is not a defined access");
            }
        }

        /// <summary>The blob options are positive, and the upload ceiling admits every kind.</summary>
        /// <param name="services">The gate's scoped services.</param>
        /// <param name="kinds">The kind registry.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckOptions(IServiceProvider services, IBlobKindRegistry kinds, List<CompositionProblem> problems)
        {
            BlobOptions options;
            try
            {
                options = services.GetRequiredService<IOptions<BlobOptions>>().Value;
            }
            catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
            {
                // The options check reports options that cannot be bound or validated.
                return;
            }

            string fix = $"Set the value under '{BlobOptions.SectionPath}' to a positive one, or remove it to use the default.";
            void Positive(string name, bool positive, string value)
            {
                if (!positive)
                {
                    problems.Add(new CompositionProblem(Name, $"The blob option {name} is {value}, which is not positive (a lifetime also at most {int.MaxValue.ToString(CultureInfo.InvariantCulture)} seconds).", fix));
                }
            }

            // --- Every value positive; the lifetimes in whole seconds a statement can bind ---
            Positive(nameof(BlobOptions.StagingTtl), IsSeconds(options.StagingTtl), options.StagingTtl.ToString());
            Positive(nameof(BlobOptions.SweepBatchSize), options.SweepBatchSize > 0, options.SweepBatchSize.ToString(CultureInfo.InvariantCulture));
            Positive(nameof(BlobOptions.SweepReclaimAfter), IsSeconds(options.SweepReclaimAfter), options.SweepReclaimAfter.ToString());
            Positive(nameof(BlobOptions.MaxStagedPerUser), options.MaxStagedPerUser > 0, options.MaxStagedPerUser.ToString(CultureInfo.InvariantCulture));
            Positive(nameof(BlobOptions.MaxStagedBytesPerUser), options.MaxStagedBytesPerUser > 0, options.MaxStagedBytesPerUser.ToString(CultureInfo.InvariantCulture));
            Positive(nameof(BlobOptions.MaxUploadSize), options.MaxUploadSize > 0, options.MaxUploadSize.ToString(CultureInfo.InvariantCulture));
            Positive(nameof(BlobOptions.ThumbnailTimeout), options.ThumbnailTimeout > TimeSpan.Zero, options.ThumbnailTimeout.ToString());
            Positive(nameof(BlobOptions.ImageProcessTimeout), options.ImageProcessTimeout > TimeSpan.Zero, options.ImageProcessTimeout.ToString());

            // --- The ceiling: a kind larger than it is a problem, never a silent clamp (spec 0017 §9) ---
            foreach (BlobKindDescriptor kind in kinds.All.Where(kind => kind.Policy.MaxSize > options.MaxUploadSize))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The blob kind '{kind.Kind}' accepts up to {kind.Policy.MaxSize.ToString(CultureInfo.InvariantCulture)} bytes, above the blob option {nameof(BlobOptions.MaxUploadSize)} of {options.MaxUploadSize.ToString(CultureInfo.InvariantCulture)} bytes.",
                    $"Raise '{BlobOptions.SectionPath}:{nameof(BlobOptions.MaxUploadSize)}', or narrow the kind's MaxSizeBytes or its transform."));
            }
        }

        /// <summary>The file-system store's root is valid and can publish create-only, when it is the store.</summary>
        /// <param name="services">The gate's scoped services.</param>
        /// <param name="registrations">The blob registrations.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckFileSystemStore(IServiceProvider services, BlobRegistrations registrations, List<CompositionProblem> problems)
        {
            if (!registrations.Stores.Any(static store => store.ImplementationType == typeof(FileSystemBlobStore)))
            {
                return;
            }

            string fix = $"Set '{FileSystemBlobStoreOptions.SectionName}:{nameof(FileSystemBlobStoreOptions.RootPath)}' to an absolute directory on a local file system the process can write.";

            // --- The root: resolving the options runs the store's own validator ---
            FileSystemBlobStoreOptions options;
            try
            {
                options = services.GetRequiredService<IOptions<FileSystemBlobStoreOptions>>().Value;
            }
            catch (OptionsValidationException exception)
            {
                foreach (string failure in exception.Failures)
                {
                    problems.Add(new CompositionProblem(Name, failure, fix));
                }

                return;
            }
            catch (InvalidOperationException exception)
            {
                problems.Add(new CompositionProblem(Name, $"The file-system blob store's options cannot be bound: {exception.Message}", fix));
                return;
            }

            // --- Create-only publish; the temporary directory sits under the root, so it shares the volume ---
            if (options.RootPath is string root && FileSystemBlobStore.ProbeCreateOnlyPublish(root) is string unsupported)
            {
                problems.Add(new CompositionProblem(Name, unsupported, fix));
            }
        }

        /// <summary>A configured scratch directory is absolute: a relative one would follow the working directory.</summary>
        /// <param name="scratch">The scratch directory.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckScratch(ScratchDirectory scratch, List<CompositionProblem> problems)
        {
            if (scratch.ConfiguredPath is string path && !Path.IsPathFullyQualified(path))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The scratch directory '{path}' configured under '{ScratchDirectory.ConfigurationKey}' is not an absolute path.",
                    $"Set '{ScratchDirectory.ConfigurationKey}' to an absolute directory, or remove it to use the system's temporary directory."));
            }
        }

        /// <summary>Whether a lifetime is positive and its seconds, rounded up, fit a statement's <c>int</c>.</summary>
        /// <param name="value">The lifetime.</param>
        /// <returns>Whether it can be bound.</returns>
        private static bool IsSeconds(TimeSpan value)
        {
            return value > TimeSpan.Zero && Math.Ceiling(value.TotalSeconds) <= int.MaxValue;
        }

        /// <summary>The registrations, described and joined.</summary>
        /// <param name="descriptors">The registrations.</param>
        /// <returns>The description.</returns>
        private static string DescribeAll(IEnumerable<ServiceDescriptor> descriptors)
        {
            return string.Join(", ", descriptors.Select(BlobRegistrations.Describe));
        }
    }
}
