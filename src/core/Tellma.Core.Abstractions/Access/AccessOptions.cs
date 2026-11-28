// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The limits and timings of users, roles and permissions, bound from <c>Tellma:Access</c> and
    ///     validated by the startup gate. A deployment changes them in configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     Every count is at least 1 and every span positive; <see cref="MaxFilterLength" /> is at most
    ///     2048, the width of the stored filter; <see cref="FastDenyWindow" /> is at most
    ///     <see cref="PermissionsMaxAge" />; <see cref="ActivityStampInterval" /> is at least 60
    ///     seconds, the throttle the database side applies whatever this says. The cache sizes are the
    ///     cache options', never repeated here.
    /// </remarks>
    public sealed class AccessOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Access";

        /// <summary>The widest a stored filter may be, the width of its column.</summary>
        public const int FilterColumnLength = 2048;

        /// <summary>The shortest the activity stamp interval may be: the database side's own throttle.</summary>
        private static readonly TimeSpan MinActivityStampInterval = TimeSpan.FromSeconds(60);

        /// <summary>The most permissions one role may hold; 300 by default.</summary>
        [Range(1, int.MaxValue)]
        public int MaxPermissionsPerRole { get; set; } = 300;

        /// <summary>The most roles one user may belong to; 32 by default.</summary>
        [Range(1, int.MaxValue)]
        public int MaxRolesPerUser { get; set; } = 32;

        /// <summary>The longest a permission's filter may be, in characters; 2048 by default, and at most 2048.</summary>
        [Range(1, FilterColumnLength)]
        public int MaxFilterLength { get; set; } = FilterColumnLength;

        /// <summary>
        ///     The oldest a cached permission set may be before the next request reloads it, bounding
        ///     the damage of an edit that forgot its version-tag bump; two hours by default.
        /// </summary>
        public TimeSpan PermissionsMaxAge { get; set; } = TimeSpan.FromHours(2);

        /// <summary>
        ///     How long this instance waits between two activity stamps of one user; 60 seconds by
        ///     default, and at least 60.
        /// </summary>
        public TimeSpan ActivityStampInterval { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        ///     The age of a cached set beyond which a denial is verified again against the database
        ///     before it is returned; five seconds by default, at most <see cref="PermissionsMaxAge" />.
        /// </summary>
        public TimeSpan FastDenyWindow { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>The most keys one user's preferences may hold; 1024 by default.</summary>
        [Range(1, int.MaxValue)]
        public int MaxPreferenceKeys { get; set; } = 1024;

        /// <summary>The largest one preference value may be, in bytes; 32 KiB by default.</summary>
        [Range(1, int.MaxValue)]
        public int MaxPreferenceValueBytes { get; set; } = 32768;

        /// <summary>How long a security write waits for the lock that serialises security writes; five seconds by default.</summary>
        public TimeSpan SecurityLockTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>The most holders the "who holds this" answer lists; 500 by default.</summary>
        [Range(1, int.MaxValue)]
        public int MaxHolders { get; set; } = 500;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Every span is positive ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(PermissionsMaxAge), PermissionsMaxAge),
                (nameof(FastDenyWindow), FastDenyWindow),
                (nameof(SecurityLockTimeout), SecurityLockTimeout),
            })
            {
                if (value <= TimeSpan.Zero)
                {
                    yield return Problem(name, value, "positive");
                }
            }

            // --- A denial is re-verified within the age a set may reach at all ---
            if (FastDenyWindow > TimeSpan.Zero && PermissionsMaxAge > TimeSpan.Zero && FastDenyWindow > PermissionsMaxAge)
            {
                yield return Problem(nameof(FastDenyWindow), FastDenyWindow, $"at most {nameof(PermissionsMaxAge)}");
            }

            // --- The stamp interval cannot undercut the database side's throttle ---
            if (ActivityStampInterval < MinActivityStampInterval)
            {
                yield return Problem(nameof(ActivityStampInterval), ActivityStampInterval, "at least 60 seconds");
            }
        }

        /// <summary>Describes a value outside its bounds, naming its configuration key.</summary>
        /// <param name="name">The option's name.</param>
        /// <param name="value">Its value.</param>
        /// <param name="bounds">The bounds it must fall within, as words.</param>
        /// <returns>The result, naming the option as its member.</returns>
        private static ValidationResult Problem(string name, TimeSpan value, string bounds)
        {
            return new ValidationResult(
                string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{name} is {value:c}; it must be {bounds}."),
                [name]);
        }
    }
}
