// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.Localization;

namespace Tellma.Core.AspNetCore.Problems
{
    // Spec 0016 §7.2: title, detail and every message are rendered for readers without a string
    // pack; the SPA renders the codes and their arguments from its pack instead.
    /// <summary>
    ///     Renders a problem's body in the reader's culture: its title and detail from the code's
    ///     <c>Problem_</c> resources, each validation item's message from its code's resource, and a
    ///     refused permission's resource and action as their labels. Argument values stay raw.
    /// </summary>
    /// <remarks>
    ///     The reader's culture is the flow's entered localization scope, else the current UI
    ///     culture, as for every localizer; the caller enters the request's before rendering.
    /// </remarks>
    internal static class ProblemRenderer
    {
        /// <summary>The prefix of a problem code's resources.</summary>
        public const string ResourcePrefix = "Problem_";

        /// <summary>The suffix of a problem code's detail resource.</summary>
        public const string DetailSuffix = "_Detail";

        /// <summary>The argument a validation message names the property in.</summary>
        private const string PropertyArgument = "property";

        /// <summary>The empty argument set.</summary>
        private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>The resource key of a problem code's title: <c>Problem_</c> and the code with each <c>-</c> written <c>_</c>.</summary>
        /// <param name="code">The code, such as <c>tenant-not-found</c>.</param>
        /// <returns>The key, such as <c>Problem_tenant_not_found</c>.</returns>
        public static string TitleKey(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            return ResourcePrefix + code.Replace('-', '_');
        }

        /// <summary>Renders a problem's body.</summary>
        /// <param name="problem">The problem.</param>
        /// <param name="services">The request's services, which hold the resources and the labels.</param>
        /// <param name="instance">The request's trace id.</param>
        /// <returns>The body.</returns>
        public static ProblemDocument Render(ProblemDescription problem, IServiceProvider services, string instance)
        {
            ArgumentNullException.ThrowIfNull(problem);
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(instance);

            // A host without the platform's localization still answers a well-formed body, its codes as its texts.
            ResourceTexts? texts = services.GetService<ResourceTexts>();
            ApiOptions api = services.GetService<IOptions<ApiOptions>>()?.Value ?? new ApiOptions();
            string titleKey = TitleKey(problem.Code);

            return new ProblemDocument(
                api.ProblemTypeBase + problem.Code,
                texts?.Render(titleKey) ?? problem.Code,
                problem.Status,
                texts is null ? null : Detail(problem, titleKey + DetailSuffix, texts),
                instance,
                problem.Code,
                [.. problem.Errors.Select(error => Error(error, texts, services))],
                problem.ErrorDetails,
                problem.Arguments);
        }

        /// <summary>The detail: the raise site's sentence, another resource's text, or the code's detail resource.</summary>
        /// <param name="problem">The problem.</param>
        /// <param name="key">The code's detail resource.</param>
        /// <param name="texts">The resources.</param>
        /// <returns>The detail, or null when there is none.</returns>
        private static string? Detail(ProblemDescription problem, string key, ResourceTexts texts)
        {
            return problem.Detail switch
            {
                ProblemDetail.Verbatim verbatim => verbatim.Text,
                ProblemDetail.Resource resource => texts.Render(resource.Key, resource.Arguments) ?? Conventional(key, problem.Arguments, texts),
                ProblemDetail.Securable securable => Conventional(key, SecurableArguments(securable, texts), texts),
                null => Conventional(key, problem.Arguments, texts),
                _ => null,
            };
        }

        /// <summary>The code's own detail resource, when a feature declares one.</summary>
        /// <param name="key">The resource key.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <param name="texts">The resources.</param>
        /// <returns>The text, or null.</returns>
        private static string? Conventional(string key, IReadOnlyDictionary<string, object?>? arguments, ResourceTexts texts)
        {
            return texts.Declares(key) ? texts.Render(key, arguments ?? NoArguments) : null;
        }

        /// <summary>A refused permission's detail arguments: the reason, the path, and the resource and action as labels.</summary>
        /// <param name="securable">The refusal.</param>
        /// <param name="texts">The resources.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> SecurableArguments(ProblemDetail.Securable securable, ResourceTexts texts)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["reason"] = securable.Reason,
                ["resource"] = texts.ResourceLabel(securable.SecurableResource),
                ["action"] = securable.Action is { } action ? texts.ActionLabel(securable.SecurableResource, action) : null,
                ["path"] = securable.Path,
            };
        }

        /// <summary>One validation item, its message rendered with its property named by its label.</summary>
        /// <param name="error">The item.</param>
        /// <param name="texts">The resources, or null.</param>
        /// <param name="services">The request's services, which hold the labels.</param>
        /// <returns>The item's body.</returns>
        private static ProblemErrorDocument Error(ProblemError error, ResourceTexts? texts, IServiceProvider services)
        {
            string? message = texts?.Render(error.MessageKey, MessageArguments(error, services));
            return new ProblemErrorDocument(
                error.Path,
                error.Sheet,
                error.Row,
                error.Column,
                error.Header,
                error.Property,
                error.Code,
                message ?? error.Code,
                error.Arguments);
        }

        /// <summary>A message's arguments: the item's own, its property argument replaced by the property's label where the entity is known.</summary>
        /// <param name="error">The item.</param>
        /// <param name="services">The request's services.</param>
        /// <returns>The arguments.</returns>
        private static IReadOnlyDictionary<string, object?> MessageArguments(ProblemError error, IServiceProvider services)
        {
            // --- Only a property of a model entity has a label; any other keeps its name ---
            if (error.Entity is not Type entity
                || error.Arguments.GetValueOrDefault(PropertyArgument) is not string property
                || services.GetService<ILabelProvider>() is not ILabelProvider labels
                || services.GetService<IEntityMetadataProvider>() is not IEntityMetadataProvider metadata
                || !metadata.All.Any(model => model.ClrType == entity))
            {
                return error.Arguments;
            }

            Dictionary<string, object?> arguments = new(error.Arguments, StringComparer.Ordinal)
            {
                [PropertyArgument] = labels.PropertyLabel(entity, property),
            };
            return arguments;
        }
    }
}
