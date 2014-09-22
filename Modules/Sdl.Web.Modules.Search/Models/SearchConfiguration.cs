﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sdl.Web.Modules.Search
{
    public class SearchConfiguration
    {
        public SearchConfiguration()
        {
            SearchBoxPlaceholderText = "Search";
        }
        public String ResultsLink { get; set; }
        public String SearchBoxPlaceholderText { get; set; }
    }
}