using System.IO;

namespace TESA.Runner.ProjectService
{
    internal static class Constants
    {
        internal static string RunnerProcess = "TESA.Runner.MainPro";

        internal static string RunnerEnviromentVariableName = "TESA.Runner.Link";

        internal static string ExecutableExtension = ".exe";

        internal static string RunnerServiceName = "TESA.Runner.ProjectService";

        internal static string RunnerLocation = Path.Combine(AppContext.BaseDirectory,
            Path.Combine(Constants.RunnerProcess + Constants.ExecutableExtension));

    }
}
