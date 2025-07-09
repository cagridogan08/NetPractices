
namespace ProjectExporterMvvm.Helpers
{
    /// <summary>
    /// Helper for running async code synchronously in testing scenarios
    /// </summary>
    public static class AsyncHelper
    {
        private static readonly TaskFactory TaskFactory = new(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        /// <summary>
        /// Executes an async Task method synchronously
        /// </summary>
        public static void RunSync(Func<Task> func)
        {
            TaskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes an async Task&lt;T&gt; method synchronously
        /// </summary>
        public static TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            return TaskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }
    }
}
