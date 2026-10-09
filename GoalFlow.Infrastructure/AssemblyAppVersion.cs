using GoalFlow.Application;

namespace GoalFlow.Infrastructure;

public sealed class AssemblyAppVersion : IAppVersion
{
    public string Version { get; } =
        AssemblyReference.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
