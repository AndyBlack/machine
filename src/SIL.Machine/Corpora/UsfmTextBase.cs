﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using SIL.Machine.Tokenization;
using SIL.Scripture;

namespace SIL.Machine.Corpora
{
	public abstract class UsfmTextBase : ScriptureTextBase
	{
		private static readonly HashSet<string> NonVerseParaStyles = new HashSet<string>
		{
			"ms", "mr", "s", "sr", "r", "d", "sp", "rem"
		};

		private readonly UsfmParser _parser;
		private readonly Encoding _encoding;

		protected UsfmTextBase(ITokenizer<string, int, string> wordTokenizer, string id, UsfmStylesheet stylesheet,
			Encoding encoding, ScrVers versification)
			: base(wordTokenizer, id, versification)
		{
			_parser = new UsfmParser(stylesheet);
			_encoding = encoding;
		}

		public override IEnumerable<TextSegment> Segments
		{
			get
			{
				string usfm = ReadUsfm();
				bool inVerse = false;
				UsfmMarker curEmbedMarker = null;
				var sb = new StringBuilder();
				string chapter = null, verse = null;
				bool sentenceStart = true;
				foreach (UsfmToken token in _parser.Parse(usfm))
				{
					switch (token.Type)
					{
						case UsfmTokenType.Chapter:
							if (inVerse)
							{
								string text = sb.ToString();
								foreach (TextSegment seg in CreateTextSegments(chapter, verse, text, sentenceStart))
									yield return seg;
								sentenceStart = true;
								sb.Clear();
								inVerse = false;
							}
							chapter = token.Text;
							verse = null;
							break;

						case UsfmTokenType.Verse:
							if (inVerse)
							{
								string text = sb.ToString();
								foreach (TextSegment seg in CreateTextSegments(chapter, verse, text, sentenceStart))
									yield return seg;
								sentenceStart = text.HasSentenceEnding();
								sb.Clear();
							}
							else
							{
								inVerse = true;
							}
							verse = token.Text;
							break;

						case UsfmTokenType.Paragraph:
							if (!IsVersePara(token) && inVerse)
							{
								string text = sb.ToString();
                                foreach (TextSegment seg in CreateTextSegments(chapter, verse, text, sentenceStart))
                                    yield return seg;
								sentenceStart = true;
								sb.Clear();
								inVerse = false;
								verse = null;
							}
							break;

						case UsfmTokenType.Note:
							curEmbedMarker = token.Marker;
							break;

						case UsfmTokenType.End:
							if (curEmbedMarker != null && token.Marker.Marker == curEmbedMarker.EndMarker)
								curEmbedMarker = null;
							break;

						case UsfmTokenType.Character:
							switch (token.Marker.Marker)
							{
								case "fig":
								case "va":
								case "vp":
									curEmbedMarker = token.Marker;
									break;
							}
							break;

						case UsfmTokenType.Text:
							if (inVerse && curEmbedMarker == null && !string.IsNullOrEmpty(token.Text))
								sb.Append(token.Text);
							break;
					}
				}

				if (inVerse)
				{
					foreach (TextSegment seg in CreateTextSegments(chapter, verse, sb.ToString(), sentenceStart))
						yield return seg;
				}
			}
		}

		private string ReadUsfm()
		{
			using (IStreamContainer streamContainer = CreateStreamContainer())
			using (var reader = new StreamReader(streamContainer.OpenStream(), _encoding))
			{
				return reader.ReadToEnd();
			}
		}

		private static bool IsVersePara(UsfmToken paraToken)
		{
			string style = paraToken.Marker.Marker;
			if (NonVerseParaStyles.Contains(style))
				return false;

			if (IsNumberedStyle("ms", style))
				return false;

			if (IsNumberedStyle("s", style))
				return false;

			return true;
		}

		private static bool IsNumberedStyle(string stylePrefix, string style)
		{
			return style.StartsWith(stylePrefix) && int.TryParse(style.Substring(stylePrefix.Length), out _);
		}
	}
}
