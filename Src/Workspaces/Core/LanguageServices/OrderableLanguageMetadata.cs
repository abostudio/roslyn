﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.LanguageServices
{
    internal class OrderableLanguageMetadata : OrderableMetadata, ILanguageMetadata
    {
        public string Language { get; private set; }

        public OrderableLanguageMetadata(IDictionary<string, object> data)
            : base(data)
        {
            this.Language = (string)data.GetValueOrDefault("Language");
        }

        public OrderableLanguageMetadata(string name, string language, IEnumerable<string> after = null, IEnumerable<string> before = null)
            : base(name, after, before)
        {
            this.Language = language;
        }
    }
}