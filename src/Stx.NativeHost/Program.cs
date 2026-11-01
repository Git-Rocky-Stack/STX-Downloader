using System.Diagnostics;
using Stx.Bridge;

// Chrome starts this for each click and reads its first reply. It must write nothing else to stdout.
string app = Path.Combine(AppContext.BaseDirectory, "STXDownloader.exe");

void Launch()
{
    // Through the shell, so the app doesn't inherit this process's stdin and stdout and hold Chrome's pipes open after this exits.
    if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
}

await using var input = Console.OpenStandardInput();
await using var output = Console.OpenStandardOutput();
await NativeHostRelay.RunAsync(input, output, PipeServer.NameForCurrentUser(), Launch, TimeSpan.FromSeconds(30));
