using SIL.Machine.Annotations;
using SIL.Machine.Matching;

namespace SIL.Machine.Rules
{
    public interface IPatternRuleSpec<TData, TOffset>
        where TData : IAnnotatedData<TOffset>
    {
        Pattern<TData, TOffset> Pattern { get; }

        bool IsApplicable(TData input);

        TData ApplyRhs(PatternRule<TData, TOffset> rule, Match<TData, TOffset> match);
    }
}
