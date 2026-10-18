using System.Text;
using TwelfthVoice.Cli;

Console.OutputEncoding = Encoding.UTF8;

var cli = CliArgs.Parse(args);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return cli.Command switch
    {
        "teams" => Commands.Teams(),
        "generate" => Commands.Generate(cli),
        "recap" => Commands.Recap(cli),
        "replay" => await Commands.ReplayAsync(cli, cancellation.Token),
        _ => Commands.Help()
    };
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Stopped.");
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
