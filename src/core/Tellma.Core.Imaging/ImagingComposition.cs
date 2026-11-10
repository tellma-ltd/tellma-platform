// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Imaging
{
    /// <summary>The host's registration of the SkiaSharp image processor.</summary>
    public static class ImagingComposition
    {
        /// <summary>
        ///     Registers <see cref="SkiaImageProcessor" /> as the process's one
        ///     <see cref="IImageProcessor" />, its options bound from <c>Tellma:Blobs</c>
        ///     (<see cref="SkiaImageProcessorOptions.ImageConcurrency" />,
        ///     <see cref="SkiaImageProcessorOptions.PdfWorkerConcurrency" />,
        ///     <see cref="SkiaImageProcessorOptions.PdfWorkerMemoryLimit" />), and the
        ///     <c>core.imaging</c> startup check that loads Skia and starts a PDF worker before the
        ///     host serves traffic.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A process runs on at most one image processor, so no other processor is registered
        ///         beside this one; a distribution preferring another library registers that instead
        ///         and drops this package. Calling it twice registers the processor once.
        ///     </para>
        ///     <para>
        ///         PDF thumbnails render on Linux hosts with glibc, the production shape; a host on
        ///         Windows or macOS renders them only in the Development environment, which the
        ///         processor reads from the host's <c>IHostEnvironment</c> when one is registered. A
        ///         host that renders none says so once in its log, at startup.
        ///     </para>
        /// </remarks>
        /// <param name="services">The host's services.</param>
        /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
        public static void AddTellmaSkia(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // A second call adds nothing: two registrations would read as two processors.
            bool registered = services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IImageProcessor) && descriptor.ImplementationType == typeof(SkiaImageProcessor));
            if (registered)
            {
                return;
            }

            // AddMetrics is idempotent and supplies the IMeterFactory the image instruments are
            // created from; the clock is the system's unless the host registered its own.
            services.AddMetrics();
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptions<SkiaImageProcessorOptions>().BindConfiguration(SkiaImageProcessorOptions.SectionName);
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<SkiaImageProcessorOptions>, SkiaImageProcessorOptionsValidator>());
            services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupCheck, ImagingStartupCheck>());
        }
    }
}
