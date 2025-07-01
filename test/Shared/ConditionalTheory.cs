using Xunit;

namespace Dnvm.Test;

public class ConditionalTheoryAttribute : TheoryAttribute
{
    public ConditionalTheoryAttribute(params Type[] conditions)
    {
        foreach (var condType in conditions)
        {
            var cond = (ExecutionCondition)Activator.CreateInstance(condType)!;
            if (cond.ShouldSkip)
            {
                base.Skip = cond.SkipReason;
            }
        }
    }
}