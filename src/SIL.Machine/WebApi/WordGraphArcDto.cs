﻿using SIL.Machine.Translation;

namespace SIL.Machine.WebApi
{
    public class WordGraphArcDto
    {
        public int PrevState { get; set; }
        public int NextState { get; set; }
        public float Score { get; set; }
        public string[] Words { get; set; }
        public float[] Confidences { get; set; }
        public RangeDto SourceSegmentRange { get; set; }
        public AlignedWordPairDto[] Alignment { get; set; }
        public TranslationSources[] Sources { get; set; }
    }
}
