using System.Runtime.CompilerServices;

namespace Stx.Core.Tests;

/// <summary>Tests provoke failures on purpose. What they log goes to a file of their own, never the user's error log.</summary>
static class TestErrorLog
{
    [ModuleInitializer]
    internal static void KeepOutOfTheUsersLog() =>
        Stx.Core.Logging.ErrorLog.FilePath = Path.Combine(Path.GetTempPath(), "stx-tests", "Core", "errors.log");
}
