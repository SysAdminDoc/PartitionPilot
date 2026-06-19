using PartitionPilot;

namespace PartitionPilot.Cli;

internal sealed class ConsoleLog : IActivityLog
{
    public void Log(string message) => Console.Error.WriteLine(message);
}
