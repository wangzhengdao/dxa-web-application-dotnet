﻿namespace Sdl.Web.Common.Models
{
    [SemanticEntity("http://schema.org", "Thing", "s")]
    public class Image : MediaItem
    {
        [SemanticProperty("s:name")]
        public string AlternateText { get; set; }
    }
}