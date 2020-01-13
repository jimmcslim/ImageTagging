using System;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace ImageTagging
{
    public static class ProcessImage
    {
        [FunctionName("ProcessImage")]
        // ReSharper disable once UnusedMember.Global
        public static async Task Function([ActivityTrigger] string path, ILogger log)
        {
            log.LogInformation($"Processing image {path}.");

            var endpoint = Environment.GetEnvironmentVariable("COMPUTER_VISION_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("COMPUTER_VISION_SUBSCRIPTION_KEY");
            var creds = new ApiKeyServiceClientCredentials(key);
            var client = new ComputerVisionClient(creds)
            {
                Endpoint = endpoint
            };
            string uri = "";
            //var result = await client.DescribeImageWithHttpMessagesAsync(uri);
            
            //Serialize
            return;
        }
    }
}