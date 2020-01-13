using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace ImageTagging
{
    public static class ProcessBlobs
    {
        [FunctionName("ProcessBlobs")]
        // ReSharper disable once UnusedMember.Global
        public static async Task RunOrchestrator([OrchestrationTrigger] DurableOrchestrationContext context,
            ILogger log)
        {
            var path = context.GetInput<string>();
            var result = await context.CallActivityAsync<List<StorageAccountItem>>("EnumerateBlobs", path);

            foreach (var item in result)
            {
                await ProcessItem(context, item, log);
            }
        }

        private static async Task ProcessItem(DurableOrchestrationContext context, StorageAccountItem item, ILogger log)
        {
            switch (item)
            {
                case Directory d:
                    log.LogInformation($"Recursing into {d.Path}");
                    await context.CallSubOrchestratorAsync("ProcessBlobs", d.Path);
                    break;
                case Image p:
                    // Actually process the photo.
                    await context.CallActivityAsync("ProcessImage", p.Path);
                    break;
            }
        }
    }
}