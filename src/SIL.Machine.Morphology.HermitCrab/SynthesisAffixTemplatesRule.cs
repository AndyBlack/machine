﻿using System.Collections.Generic;
using System.Linq;
using SIL.Machine.Annotations;
using SIL.Machine.FeatureModel;
using SIL.Machine.Rules;
using SIL.ObjectModel;

namespace SIL.Machine.Morphology.HermitCrab
{
    internal class SynthesisAffixTemplatesRule : IRule<Word, ShapeNode>
    {
        private readonly Morpher _morpher;
        private readonly Stratum _stratum;
        private readonly List<AffixTemplate> _templates;
        private readonly List<IRule<Word, ShapeNode>> _templateRules;

        public SynthesisAffixTemplatesRule(Morpher morpher, Stratum stratum)
        {
            _morpher = morpher;
            _stratum = stratum;
            _templates = stratum.AffixTemplates.ToList();
            _templateRules = _templates.Select(temp => temp.CompileSynthesisRule(morpher)).ToList();
        }

        public IEnumerable<Word> Apply(Word input)
        {
            if (!input.RealizationalFeatureStruct.IsUnifiable(input.SyntacticFeatureStruct))
                return Enumerable.Empty<Word>();

            var output = new HashSet<Word>(FreezableEqualityComparer<Word>.Default);
            bool applicableTemplate = false;
            input = ChooseInflectionalStem(input);
            for (int i = 0; i < _templateRules.Count; i++)
            {
                if (
                    _morpher.RuleSelector(_templates[i])
                    && input.SyntacticFeatureStruct.IsUnifiable(_templates[i].RequiredSyntacticFeatureStruct)
                    && !input.RootAllomorph.Morpheme.IsPartial
                )
                {
                    applicableTemplate = true;
                    foreach (Word outWord in _templateRules[i].Apply(input))
                    {
                        Word word = outWord;
                        bool templateFinal = outWord.IsPartial || _templates[i].IsFinal;
                        if (word.IsLastAppliedRuleFinal != templateFinal)
                        {
                            word = outWord.Clone();
                            word.IsLastAppliedRuleFinal = templateFinal;
                            word.Freeze();
                        }
                        output.Add(word);
                    }
                }
            }

            if (output.Count == 0)
            {
                if (!input.IsPartial && applicableTemplate)
                {
                    if (_morpher.TraceManager.IsTracing)
                        _morpher.TraceManager.ApplicableTemplatesNotApplied(_stratum, input);
                }
                else
                {
                    Word word = input;
                    if (!word.IsLastAppliedRuleFinal.HasValue || !word.IsLastAppliedRuleFinal.Value)
                    {
                        word = input.Clone();
                        word.IsLastAppliedRuleFinal = true;
                        word.Freeze();
                    }
                    output.Add(word);
                }
            }

            return output;
        }

        private Word ChooseInflectionalStem(Word input)
        {
            LexFamily family = ((LexEntry)input.RootAllomorph.Morpheme).Family;
            if (family == null || input.RealizationalFeatureStruct.IsEmpty)
                return input;

            Word best = input;
            foreach (LexEntry relative in family.Entries)
            {
                if (
                    relative != input.RootAllomorph.Morpheme
                    && relative.Stratum == input.Stratum
                    && input.RealizationalFeatureStruct.IsUnifiable(relative.SyntacticFeatureStruct)
                    && best.SyntacticFeatureStruct.Subsumes(relative.SyntacticFeatureStruct)
                )
                {
                    FeatureStruct remainder = relative.SyntacticFeatureStruct.Clone();
                    remainder.Subtract(best.SyntacticFeatureStruct);
                    if (!remainder.IsEmpty && input.RealizationalFeatureStruct.IsUnifiable(remainder))
                    {
                        best = new Word(relative.PrimaryAllomorph, input.RealizationalFeatureStruct.Clone())
                        {
                            CurrentTrace = input.CurrentTrace
                        };
                        best.Freeze();
                    }
                }
            }

            if (_morpher.TraceManager.IsTracing && best != input)
                _morpher.TraceManager.Blocked(_stratum, best);
            return best;
        }
    }
}
