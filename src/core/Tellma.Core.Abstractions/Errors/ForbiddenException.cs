// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The caller may not perform the operation: it names the refused securable and why. Maps to
    ///     403 with the problem code <c>forbidden</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="TellmaException.Arguments" /> carries <c>resource</c>, <c>action</c>,
    ///     <c>reason</c> (kebab-case: <c>no-grant</c>, <c>outside-filter</c>,
    ///     <c>restricted-path</c>) and, for <see cref="ForbiddenReason.RestrictedPath" />,
    ///     <c>path</c>. The exception never names roles, filters or other users; an administrator
    ///     asks the access check for those.
    /// </remarks>
    /// <param name="code">The problem code: <c>forbidden</c> at every platform raise site.</param>
    /// <param name="resource">The refused securable's resource, such as <c>gl.Center</c>.</param>
    /// <param name="action">The refused securable's action, such as <c>Save</c>; null when the refusal names none.</param>
    /// <param name="reason">Why the securable was refused.</param>
    /// <param name="path">The path a <see cref="ForbiddenReason.RestrictedPath" /> refusal read; null for every other reason.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="code" /> or <paramref name="resource" /> is null, empty or white space;
    ///     <paramref name="reason" /> is undefined; or <paramref name="path" /> is missing for a
    ///     restricted path or present for another reason.
    /// </exception>
    public sealed class ForbiddenException(string code, string resource, string? action, ForbiddenReason reason, string? path = null)
        : TellmaException(code, Describe(resource, action, reason, path), ArgumentsOf(resource, action, reason, path))
    {
        /// <summary>The refused securable's resource, such as <c>gl.Center</c>.</summary>
        public string Resource { get; } = ValidateResource(resource);

        /// <summary>The refused securable's action, such as <c>Save</c>; null when the refusal names none.</summary>
        public string? Action { get; } = action;

        /// <summary>Why the securable was refused.</summary>
        public ForbiddenReason Reason { get; } = ValidateReason(reason, path);

        /// <summary>The path a <see cref="ForbiddenReason.RestrictedPath" /> refusal read; null for every other reason.</summary>
        public string? Path { get; } = path;

        /// <summary>The wire form of a reason: <c>no-grant</c>, <c>outside-filter</c> or <c>restricted-path</c>.</summary>
        /// <param name="reason">The reason, already validated.</param>
        /// <returns>The kebab-case form.</returns>
        private static string WireValue(ForbiddenReason reason)
        {
            return reason switch
            {
                ForbiddenReason.NoGrant => "no-grant",
                ForbiddenReason.OutsideFilter => "outside-filter",
                ForbiddenReason.RestrictedPath => "restricted-path",
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The reason is not a defined forbidden reason."),
            };
        }

        /// <summary>Refuses an empty resource.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The resource.</returns>
        private static string ValidateResource(string resource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            return resource;
        }

        /// <summary>Refuses an undefined reason, and a path that does not match the reason.</summary>
        /// <param name="reason">The reason.</param>
        /// <param name="path">The path.</param>
        /// <returns>The reason.</returns>
        private static ForbiddenReason ValidateReason(ForbiddenReason reason, string? path)
        {
            // Only a restricted path names what was read; every other reason has nothing to name.
            bool restrictedPath = reason == ForbiddenReason.RestrictedPath;
            return !Enum.IsDefined(reason)
                ? throw new ArgumentException($"'{reason}' is not a defined forbidden reason.", nameof(reason))
                : restrictedPath && string.IsNullOrWhiteSpace(path)
                    ? throw new ArgumentException("A restricted-path refusal names the path that was read.", nameof(path))
                    : !restrictedPath && path is not null
                        ? throw new ArgumentException($"Only a restricted-path refusal names a path; this one is '{WireValue(reason)}'.", nameof(path))
                        : reason;
        }

        /// <summary>Renders the log message.</summary>
        /// <param name="resource">The resource, already validated.</param>
        /// <param name="action">The action.</param>
        /// <param name="reason">The reason, already validated.</param>
        /// <param name="path">The path.</param>
        /// <returns>The message.</returns>
        private static string Describe(string resource, string? action, ForbiddenReason reason, string? path)
        {
            string securable = action is null ? resource : $"{resource}:{action}";
            return path is null
                ? $"Forbidden: {securable} ({WireValue(reason)})."
                : $"Forbidden: {securable} ({WireValue(reason)}) reading '{path}'.";
        }

        /// <summary>The display arguments: <c>resource</c>, <c>action</c>, <c>reason</c> and, for a restricted path, <c>path</c>.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="reason">The reason.</param>
        /// <param name="path">The path.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> ArgumentsOf(string resource, string? action, ForbiddenReason reason, string? path)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
            {
                ["resource"] = resource,
                ["action"] = action,
                ["reason"] = WireValue(reason),
            };

            if (path is not null)
            {
                arguments["path"] = path;
            }

            return arguments;
        }
    }
}
