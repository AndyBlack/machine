using System.Collections.Generic;
using System.Linq;
using SIL.Collections;
using SIL.Machine;
using SIL.Machine.FeatureModel;
using SIL.Machine.Matching;
using SIL.Machine.Rules;

namespace SIL.HermitCrab.MorphologicalRules
{
	public class SynthesisAffixProcessRule : IRule<Word, ShapeNode>
	{
		private readonly SpanFactory<ShapeNode> _spanFactory;
		private readonly Morpher _morpher;
		private readonly AffixProcessRule _rule;
		private readonly List<PatternRule<Word, ShapeNode>> _rules;

		public SynthesisAffixProcessRule(SpanFactory<ShapeNode> spanFactory, Morpher morpher, AffixProcessRule rule)
		{
			_spanFactory = spanFactory;
			_morpher = morpher;
			_rule = rule;
			_rules = new List<PatternRule<Word, ShapeNode>>();
			foreach (AffixProcessAllomorph allo in rule.Allomorphs)
			{
				var ruleSpec = new SynthesisAffixProcessAllomorphRuleSpec(allo);
				_rules.Add(new PatternRule<Word, ShapeNode>(_spanFactory, ruleSpec,
					new MatcherSettings<ShapeNode>
						{
							Filter = ann => ann.Type().IsOneOf(HCFeatureSystem.Segment, HCFeatureSystem.Boundary),
							UseDefaults = true,
							AnchoredToStart = true,
							AnchoredToEnd = true
						}));
			}
		}

		public bool IsApplicable(Word input)
		{
			return input.CurrentMorphologicalRule == _rule && input.GetApplicationCount(_rule) < _rule.MaxApplicationCount;
		}

		public IEnumerable<Word> Apply(Word input)
		{
			var output = new List<Word>();
			FeatureStruct syntacticFS;
			if (_rule.RequiredSyntacticFeatureStruct.Unify(input.SyntacticFeatureStruct, true, out syntacticFS))
			{
				for (int i = 0; i < _rules.Count; i++)
				{
					Word outWord = _rules[i].Apply(input).SingleOrDefault();
					if (outWord != null)
					{
						outWord.SyntacticFeatureStruct = syntacticFS;
						outWord.SyntacticFeatureStruct.PriorityUnion(_rule.OutSyntacticFeatureStruct);

						foreach (Feature obligFeature in _rule.ObligatorySyntacticFeatures)
							outWord.ObligatorySyntacticFeatures.Add(obligFeature);

						outWord.CurrentMorphologicalRuleApplied();

						Word newWord;
						if (_rule.Blockable && outWord.CheckBlocking(out newWord))
						{
							if (_morpher.TraceBlocking)
								newWord.CurrentTrace.Children.Add(new Trace(TraceType.Blocking, _rule) { Output = newWord.DeepClone() });
							outWord = newWord;
						}

						if (_morpher.GetTraceRule(_rule))
						{
							var trace = new Trace(TraceType.MorphologicalRuleSynthesis, _rule) { Input = input.DeepClone(), Output = outWord.DeepClone() };
							outWord.CurrentTrace.Children.Add(trace);
							outWord.CurrentTrace = trace;
						}

						output.Add(outWord);

						AffixProcessAllomorph allo = _rule.Allomorphs[i];
						// TODO: check for free fluctuation
						if (allo.RequiredEnvironments.Count == 0 && allo.ExcludedEnvironments.Count == 0)
							break;
					}
				}
			}

			if (output.Count == 0 && _morpher.GetTraceRule(_rule))
				input.CurrentTrace.Children.Add(new Trace(TraceType.MorphologicalRuleSynthesis, _rule) { Input = input.DeepClone() });

			return output;
		}
	}
}
