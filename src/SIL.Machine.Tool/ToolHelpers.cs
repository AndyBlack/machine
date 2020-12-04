﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.Extensions;
using SIL.Machine.Corpora;
using SIL.Machine.Tokenization;
using SIL.Machine.Translation;
using SIL.Machine.Translation.OpenNmt;
using SIL.Machine.Translation.Thot;
using SIL.Scripture;

namespace SIL.Machine
{
	internal static class ToolHelpers
	{
		public const string Hmm = "hmm";
		public const string Ibm1 = "ibm1";
		public const string Ibm2 = "ibm2";
		public const string FastAlign = "fast_align";
		public const string Smt = "smt";
		public const string Nmt = "nmt";

		public static bool ValidateCorpusFormatOption(string value)
		{
			return string.IsNullOrEmpty(value) || value.ToLowerInvariant().IsOneOf("dbl", "usx", "text", "pt");
		}

		public static bool ValidateWordTokenizerOption(string value, bool supportsNullTokenizer = false)
		{
			var types = new HashSet<string> { "latin", "whitespace", "zwsp" };
			if (supportsNullTokenizer)
				types.Add("none");
			return string.IsNullOrEmpty(value) || types.Contains(value.ToLowerInvariant());
		}

		public static ITextCorpus CreateTextCorpus(ITokenizer<string, int, string> wordTokenizer, string type,
			string path)
		{
			switch (type.ToLowerInvariant())
			{
				case "dbl":
					return new DblBundleTextCorpus(wordTokenizer, path);

				case "usx":
					return new UsxFileTextCorpus(wordTokenizer, path);

				case "pt":
					return new ParatextTextCorpus(wordTokenizer, path);

				case "text":
					return new TextFileTextCorpus(wordTokenizer, path);
			}

			throw new ArgumentException("An invalid text corpus type was specified.", nameof(type));
		}

		public static ITextAlignmentCorpus CreateAlignmentsCorpus(string type, string path)
		{
			switch (type.ToLowerInvariant())
			{
				case "text":
					return new TextFileTextAlignmentCorpus(path);
			}

			throw new ArgumentException("An invalid alignment corpus type was specified.", nameof(type));
		}

		public static IRangeTokenizer<string, int, string> CreateWordTokenizer(string type)
		{
			switch (type.ToLowerInvariant())
			{
				case "latin":
					return new LatinWordTokenizer();

				case "none":
					return new NullTokenizer();

				case "zwsp":
					return new ZwspWordTokenizer();

				case "whitespace":
					return new WhitespaceTokenizer();
			}

			throw new ArgumentException("An invalid tokenizer type was specified.", nameof(type));
		}

		public static IDetokenizer<string, string> CreateWordDetokenizer(string type)
		{
			switch (type.ToLowerInvariant())
			{
				case "latin":
					return new LatinWordDetokenizer();

				case "zwsp":
					return new ZwspWordDetokenizer();

				case "whitespace":
					return new WhitespaceDetokenizer();
			}

			throw new ArgumentException("An invalid tokenizer type was specified.", nameof(type));
		}

		public static ISet<string> GetTexts(IEnumerable<string> values)
		{
			var ids = new HashSet<string>();
			foreach (string value in values)
			{
				foreach (string id in value.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (id == "*NT*")
						ids.UnionWith(Canon.AllBookIds.Where(i => Canon.IsBookNT(i)));
					else if (id == "*OT*")
						ids.UnionWith(Canon.AllBookIds.Where(i => Canon.IsBookOT(i)));
					else
						ids.Add(id);
				}
			}
			return ids;
		}

		public static bool IsDirectoryPath(string path)
		{
			if (Directory.Exists(path))
				return true;
			string separator1 = Path.DirectorySeparatorChar.ToString();
			string separator2 = Path.AltDirectorySeparatorChar.ToString();
			path = path.TrimEnd();
			return path.EndsWith(separator1) || path.EndsWith(separator2);
		}

		public static string GetTranslationModelConfigFileName(string path)
		{
			if (File.Exists(path))
				return path;
			else if (Directory.Exists(path) || IsDirectoryPath(path))
				return Path.Combine(path, "smt.cfg");
			else
				return path;
		}

		public static bool ValidateTranslationModelTypeOption(string value)
		{
			var validTypes = new HashSet<string> { Hmm, Ibm1, Ibm2, FastAlign, Nmt };
			return string.IsNullOrEmpty(value) || validTypes.Contains(value);
		}

		public static ITranslationModelTrainer CreateTranslationModelTrainer(string modelType,
			string modelConfigFileName, ParallelTextCorpus corpus, int maxSize)
		{
			switch (modelType)
			{
				default:
				case Hmm:
					return CreateThotSmtModelTrainer<HmmWordAlignmentModel>(modelType, modelConfigFileName, corpus,
						maxSize);
				case Ibm1:
					return CreateThotSmtModelTrainer<Ibm1WordAlignmentModel>(modelType, modelConfigFileName, corpus,
						maxSize);
				case Ibm2:
					return CreateThotSmtModelTrainer<Ibm2WordAlignmentModel>(modelType, modelConfigFileName, corpus,
						maxSize);
				case FastAlign:
					return CreateThotSmtModelTrainer<FastAlignWordAlignmentModel>(modelType, modelConfigFileName,
						corpus, maxSize);
				case Nmt:
					return new OpenNmtModelTrainer(modelConfigFileName, TokenProcessors.Lowercase,
						TokenProcessors.Lowercase, corpus, maxSize);
			}
		}

		private static void CreateConfigFile(string modelType, string modelConfigFileName)
		{
			string defaultConfigFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data",
				"default-smt.cfg");
			string text = File.ReadAllText(defaultConfigFileName);
			int emIters = 5;
			if (modelType == FastAlign)
				emIters = 4;
			text = text.Replace("{em_iters}", $"{emIters}");
			File.WriteAllText(modelConfigFileName, text);
		}

		private static ITranslationModelTrainer CreateThotSmtModelTrainer<TAlignModel>(string modelType,
			string modelConfigFileName, ParallelTextCorpus corpus, int maxSize)
			where TAlignModel : ThotWordAlignmentModelBase<TAlignModel>, new()
		{
			string modelDir = Path.GetDirectoryName(modelConfigFileName);
			if (!Directory.Exists(modelDir))
				Directory.CreateDirectory(modelDir);

			if (!File.Exists(modelConfigFileName))
				CreateConfigFile(modelType, modelConfigFileName);

			return new ThotSmtModelTrainer<TAlignModel>(modelConfigFileName, TokenProcessors.Lowercase,
				TokenProcessors.Lowercase, corpus, maxSize);
		}
	}
}
