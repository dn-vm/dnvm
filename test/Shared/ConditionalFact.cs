using Xunit;

namespace Dnvm.Test;

public class ConditionalFactAttribute : FactAttribute
{
    public ConditionalFactAttribute(params Type[] conditions)
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

public abstract class ExecutionCondition
{
    public abstract bool ShouldSkip { get; }
    public abstract string SkipReason { get; }
}

public sealed class UnixOnly : ExecutionCondition
{
    public override bool ShouldSkip => Environment.OSVersion.Platform != PlatformID.Unix;
    public override string SkipReason => "Current OS is not Unix";
}

public sealed class WindowsOnly : ExecutionCondition
{
    public override bool ShouldSkip => Environment.OSVersion.Platform != PlatformID.Win32NT;
    public override string SkipReason => "Current OS is not Windows";
}