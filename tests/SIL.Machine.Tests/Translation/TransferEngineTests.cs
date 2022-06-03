﻿using NSubstitute;
using NUnit.Framework;
using SIL.Machine.Morphology;
using SIL.ObjectModel;

namespace SIL.Machine.Translation
{
    [TestFixture]
    public class TransferEngineTests
    {
        [Test]
        public void Translate_CanTranslate_ReturnsCorrectTranslation()
        {
            var sourceAnalyzer = Substitute.For<IMorphologicalAnalyzer>();
            sourceAnalyzer.AddAnalyses(
                "habló",
                new WordAnalysis(
                    new[]
                    {
                        new TestMorpheme("s1", "v", "talk", MorphemeType.Stem),
                        new TestMorpheme("s2", "v", "pst", MorphemeType.Affix)
                    },
                    0,
                    "v"
                )
            );
            var targetGenerator = Substitute.For<IMorphologicalGenerator>();
            var targetMorphemes = new ReadOnlyObservableList<IMorpheme>(
                new ObservableList<IMorpheme>
                {
                    new TestMorpheme("e1", "v", "talk", MorphemeType.Stem),
                    new TestMorpheme("e2", "v", "pst", MorphemeType.Affix)
                }
            );
            targetGenerator.Morphemes.Returns(targetMorphemes);
            targetGenerator.AddGeneratedWords(
                new WordAnalysis(new[] { targetMorphemes[0], targetMorphemes[1] }, 0, "v"),
                "talked"
            );
            var transferer = new SimpleTransferer(new GlossMorphemeMapper(targetGenerator));
            var engine = new TransferEngine(sourceAnalyzer, transferer, targetGenerator);
            Assert.That(engine.TranslateWord("habló"), Is.EqualTo("talked".Split(' ')));
        }

        [Test]
        public void Translate_CannotAnalyze_ReturnsEmptyTranslation()
        {
            var sourceAnalyzer = Substitute.For<IMorphologicalAnalyzer>();
            sourceAnalyzer.AddAnalyses("habló");
            var targetGenerator = Substitute.For<IMorphologicalGenerator>();
            var targetMorphemes = new ReadOnlyObservableList<IMorpheme>(
                new ObservableList<IMorpheme>
                {
                    new TestMorpheme("e1", "v", "talk", MorphemeType.Stem),
                    new TestMorpheme("e2", "v", "pst", MorphemeType.Affix)
                }
            );
            targetGenerator.Morphemes.Returns(targetMorphemes);
            targetGenerator.AddGeneratedWords(
                new WordAnalysis(new[] { targetMorphemes[0], targetMorphemes[1] }, 0, "v"),
                "talked"
            );
            var transferer = new SimpleTransferer(new GlossMorphemeMapper(targetGenerator));
            var engine = new TransferEngine(sourceAnalyzer, transferer, targetGenerator);
            Assert.That(engine.TranslateWord("habló"), Is.Empty);
        }

        [Test]
        public void Translate_CannotGenerate_ReturnsEmptyTranslation()
        {
            var sourceAnalyzer = Substitute.For<IMorphologicalAnalyzer>();
            sourceAnalyzer.AddAnalyses(
                "habló",
                new WordAnalysis(
                    new[]
                    {
                        new TestMorpheme("s1", "v", "talk", MorphemeType.Stem),
                        new TestMorpheme("s2", "v", "pst", MorphemeType.Affix)
                    },
                    0,
                    "v"
                )
            );
            var targetGenerator = Substitute.For<IMorphologicalGenerator>();
            var targetMorphemes = new ReadOnlyObservableList<IMorpheme>(
                new ObservableList<IMorpheme>
                {
                    new TestMorpheme("e1", "v", "talk", MorphemeType.Stem),
                    new TestMorpheme("e2", "v", "pst", MorphemeType.Affix)
                }
            );
            targetGenerator.Morphemes.Returns(targetMorphemes);
            targetGenerator.AddGeneratedWords(
                new WordAnalysis(new[] { targetMorphemes[0], targetMorphemes[1] }, 0, "v")
            );
            var transferer = new SimpleTransferer(new GlossMorphemeMapper(targetGenerator));
            var engine = new TransferEngine(sourceAnalyzer, transferer, targetGenerator);
            Assert.That(engine.TranslateWord("habló"), Is.Empty);
        }

        [Test]
        public void Translate_CannotMapMorphemes_ReturnsEmptyTranslation()
        {
            var sourceAnalyzer = Substitute.For<IMorphologicalAnalyzer>();
            sourceAnalyzer.AddAnalyses(
                "habló",
                new WordAnalysis(
                    new[]
                    {
                        new TestMorpheme("s1", "v", "talk", MorphemeType.Stem),
                        new TestMorpheme("s2", "v", "pst", MorphemeType.Affix)
                    },
                    0,
                    "v"
                )
            );
            var targetGenerator = Substitute.For<IMorphologicalGenerator>();
            var targetMorphemes = new ReadOnlyObservableList<IMorpheme>(new ObservableList<IMorpheme>());
            targetGenerator.Morphemes.Returns(targetMorphemes);
            var transferer = new SimpleTransferer(new GlossMorphemeMapper(targetGenerator));
            var engine = new TransferEngine(sourceAnalyzer, transferer, targetGenerator);
            Assert.That(engine.TranslateWord("habló"), Is.Empty);
        }
    }
}
