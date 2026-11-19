// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>How a blob kind re-encodes the images uploaded to it.</summary>
    /// <param name="MaxDimension">The largest width or height kept, in pixels.</param>
    /// <param name="Fit">How the image fits into that dimension.</param>
    /// <param name="Format">The format it is re-encoded to.</param>
    public sealed record ClientImagePolicy(int MaxDimension, ClientImageFit Fit, ClientImageFormat Format);
}
