// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Declares an <c>int?</c> property of a stack's root, or of a child entity of its aggregate,
    ///     as a reference to a blob of one kind — the single declaration of the capability.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         From it the platform derives the column's foreign key to the blob table and its
    ///         filtered unique index (one owner row per blob), the navigation to the blob, the kind's
    ///         registration and policy, the validation that lets a save attach only a blob the saving
    ///         user staged, the confirm and release of blobs inside the save's transaction, the
    ///         download's authorisation, the Excel exclusion, and the policy the client pre-checks
    ///         uploads against.
    ///     </para>
    ///     <para>
    ///         The property stays editable: the client writes the staged blob's id, and the server
    ///         enforces the attach rule. Exactly one property in the model may declare a kind.
    ///     </para>
    /// </remarks>
    /// <param name="kind">The blob kind; it must satisfy <see cref="BlobName.IsValidKind" />.</param>
    /// <param name="preset">The policy the kind starts from.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class BlobReferenceAttribute(string kind, BlobPreset preset = BlobPreset.Attachment) : Attribute
    {
        /// <summary>The blob kind the property references.</summary>
        public string Kind { get; } = kind;

        /// <summary>The policy the kind starts from.</summary>
        public BlobPreset Preset { get; } = preset;

        /// <summary>
        ///     Narrows the preset's size limit: above 0 it is the kind's limit in bytes, and it may
        ///     not exceed the preset's; 0 keeps the preset's. Never negative. (An attribute argument
        ///     cannot be a nullable value type, so 0 stands for "not set".) A composition transform,
        ///     not the declaration, is where a limit is raised.
        /// </summary>
        public long MaxSizeBytes { get; set; }

        /// <summary>Who may download a committed blob of the kind.</summary>
        public BlobReadAccess ReadAccess { get; set; } = BlobReadAccess.OwnerRead;

        /// <summary>
        ///     The policy this declaration produces: <see cref="Preset" />'s, with
        ///     <see cref="MaxSizeBytes" /> and <see cref="ReadAccess" /> applied. A composition's
        ///     transforms apply on top of it.
        /// </summary>
        /// <returns>The declared policy.</returns>
        /// <exception cref="InvalidOperationException"><see cref="MaxSizeBytes" /> is negative or
        ///     exceeds the preset's limit.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="Preset" /> is not a defined preset.</exception>
        public BlobKindPolicy CreatePolicy()
        {
            if (MaxSizeBytes < 0)
            {
                throw new InvalidOperationException(
                    $"The blob reference of kind '{Kind}' declares a negative MaxSizeBytes ({MaxSizeBytes}); use 0 to keep the preset's limit.");
            }

            // The declaration only narrows: a larger value would loosen an upload limit (image kinds
            // are buffered whole in memory up to it), so it is refused rather than clamped.
            var preset = BlobKindPolicy.For(Preset);
            return MaxSizeBytes <= preset.MaxSize
                ? preset with
                {
                    MaxSize = MaxSizeBytes > 0 ? MaxSizeBytes : preset.MaxSize,
                    ReadAccess = ReadAccess,
                }
                : throw new InvalidOperationException(
                    $"The blob reference of kind '{Kind}' declares MaxSizeBytes {MaxSizeBytes}, above the {Preset} preset's limit of {preset.MaxSize} bytes; the declaration can only narrow the limit, and a composition transform raises it.");
        }
    }
}
