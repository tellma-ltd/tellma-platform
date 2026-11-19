// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The host's blob settings in code, reached through <see cref="TellmaBuilder.Blobs" />:
    ///     transforms of the kinds' policies, and the image processor to use instead of the SkiaSharp
    ///     one.
    /// </summary>
    public sealed class BlobsBuilder
    {
        // The composition's services, where the image processor is registered.
        private readonly IServiceCollection _services;

        // Throws once the composition method has returned, when a setting would be ignored.
        private readonly Action _throwIfSealed;

        // The host's transforms, in call order.
        private readonly List<BlobKindTransform> _transforms = [];

        /// <summary>Creates the builder over the composition's services.</summary>
        /// <param name="services">The services.</param>
        /// <param name="throwIfSealed">Throws once the composition method has returned.</param>
        internal BlobsBuilder(IServiceCollection services, Action throwIfSealed)
        {
            _services = services;
            _throwIfSealed = throwIfSealed;
        }

        /// <summary>The host's transforms, in call order; applied after every feature's.</summary>
        internal IReadOnlyList<BlobKindTransform> Transforms => _transforms;

        /// <summary>
        ///     Transforms the policy of a blob kind, such as narrowing its size limit
        ///     (<c>p =&gt; p with { MaxSize = 4 * 1024 * 1024 }</c>). Applies after every feature's
        ///     transforms, each to the result of the one before; it may return a different policy
        ///     outright, and the owner still comes from the attribute. The startup gate refuses a
        ///     kind no blob reference declares.
        /// </summary>
        /// <param name="kind">The kind, such as <c>product-image</c>.</param>
        /// <param name="configure">The transform.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="kind" /> or <paramref name="configure" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public BlobsBuilder Kind(string kind, Func<BlobKindPolicy, BlobKindPolicy> configure)
        {
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentNullException.ThrowIfNull(configure);
            _throwIfSealed();
            _transforms.Add(new BlobKindTransform(kind, configure, BlobKindTransform.HostOwner));
            return this;
        }

        /// <summary>
        ///     Registers <typeparamref name="TProcessor" /> as the process's one image processor, a
        ///     singleton: the alternative to the SkiaSharp processor's own registration. Naming the same
        ///     processor again registers it once; registering two different processors, such as this
        ///     one beside the SkiaSharp one, fails startup.
        /// </summary>
        /// <typeparam name="TProcessor">The processor.</typeparam>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public BlobsBuilder ImageProcessor<TProcessor>()
            where TProcessor : class, IImageProcessor
        {
            _throwIfSealed();

            // A different processor is registered beside any other, never replacing it: two
            // registrations are a problem the startup gate names, rather than one silently winning.
            bool registered = _services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IImageProcessor) && descriptor.ImplementationType == typeof(TProcessor));
            if (!registered)
            {
                _ = _services.AddSingleton<IImageProcessor, TProcessor>();
            }

            return this;
        }
    }
}
