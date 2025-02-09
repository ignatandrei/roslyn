﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.LanguageServices.FindUsages
{
    internal interface ISupportsNavigation
    {
        bool CanNavigateTo();
        Task NavigateToAsync(bool isPreview, bool shouldActivate, CancellationToken cancellationToken);
    }
}
