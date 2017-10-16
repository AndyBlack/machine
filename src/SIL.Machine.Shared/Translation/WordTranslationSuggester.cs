﻿using System.Collections.Generic;
using System.Linq;

namespace SIL.Machine.Translation
{
	public class WordTranslationSuggester : ITranslationSuggester
	{
		public WordTranslationSuggester(double confidenceThreshold)
		{
			ConfidenceThreshold = confidenceThreshold;
		}

		public double ConfidenceThreshold { get; set; }

		public IEnumerable<int> GetSuggestedWordIndices(int prefixCount, bool isLastWordComplete,
			TranslationResult result)
		{
			int startingJ = prefixCount;
			if (!isLastWordComplete)
			{
				// if the prefix ends with a partial word and it has been completed,
				// then make sure it is included as a suggestion,
				// otherwise, don't return any suggestions
				if ((result.WordSources[startingJ - 1] & TranslationSources.Smt) != 0)
					startingJ--;
				else
					yield break;
			}

			int lookaheadCount = ComputeLookahead(prefixCount, result);
			int j = startingJ;
			bool inPhrase = false;
			while (j < result.TargetSegment.Count && (lookaheadCount > 0 || inPhrase))
			{
				string word = result.TargetSegment[j];
				// stop suggesting at punctuation
				if (word.All(char.IsPunctuation))
					break;

				// criteria for suggesting a word
				// the word must either:
				// - meet the confidence threshold
				// - come from a transfer engine
				double confidence = result.WordConfidences[j];
				TranslationSources sources = result.WordSources[j];
				if (confidence >= ConfidenceThreshold || (sources & TranslationSources.Transfer) != 0)
				{
					yield return j;
					inPhrase = true;
					lookaheadCount--;
				}
				else
				{
					// skip over inserted words
					if (result.Alignment.IsColumnAligned(j) == AlignmentType.Aligned)
					{
						lookaheadCount--;
						// only suggest the first word/phrase we find
						if (inPhrase)
							break;
					}
				}
				j++;
			}
		}

		private int ComputeLookahead(int prefixCount, TranslationResult result)
		{
			int lookaheadCount = 1;
			int i = -1, j;
			for (j = prefixCount; j < result.TargetSegment.Count; j++)
			{
				int[] sourceIndices = result.Alignment.GetColumnAlignedIndices(j).ToArray();
				if (sourceIndices.Length == 0)
				{
					lookaheadCount++;
				}
				else
				{
					lookaheadCount += sourceIndices.Length - 1;
					foreach (int ti in sourceIndices)
					{
						if (i == -1 || ti < i)
							i = ti;
					}
				}
			}
			if (i == -1)
				i = 0;
			for (; i < result.SourceSegment.Count; i++)
			{
				if (result.Alignment.IsRowAligned(i) == AlignmentType.NotAligned)
					lookaheadCount++;
			}

			return lookaheadCount;
		}
	}
}
