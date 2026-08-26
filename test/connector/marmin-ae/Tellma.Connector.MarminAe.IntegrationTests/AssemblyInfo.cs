// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The vendor allows sixty document calls a minute per account, and this suite shares that account
// with anyone else testing against it. Running its classes one at a time keeps it far inside that
// budget by construction, and keeps the interleaved transcripts readable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
