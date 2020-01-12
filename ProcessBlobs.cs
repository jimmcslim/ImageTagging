using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

namespace ImageTagging
{
    public static class ProcessBlobs
    {
        [FunctionName("ProcessBlobs")]
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
            switch (item.Type)
            {
                case "directory":
                    log.LogInformation($"Recursing into {item.Path}");
                    await context.CallSubOrchestratorAsync("ProcessBlobs", item.Path);
                    break;
                case "blob":
                    // Actually process the photo.
                    await context.CallActivityAsync("ProcessImage", item.Path);
                    break;
            }
        }

        public class StorageAccountItem
        {
            public string Type { get; set; }
            public string Path { get; set; }
        }
        
        [FunctionName("EnumerateBlobs")]
        public static async Task<List<StorageAccountItem>> EnumerateBlobs([ActivityTrigger] string path,
            ILogger log)
        {
            var ac = StorageAccount.NewFromConnectionString(Environment.GetEnvironmentVariable("ImageStorageAccount",
                EnvironmentVariableTarget.Process));
            var blobClient = ac.CreateCloudBlobClient();

            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var containerName = pathSegments.First();
            var container = blobClient.GetContainerReference(containerName);

            var getBlobs = GetBlobsForPath(container, pathSegments, log);

            var results = new List<StorageAccountItem>();
            BlobContinuationToken continuationToken = null;
            do
            {
                var response = await getBlobs(continuationToken);
                continuationToken = response.ContinuationToken;
                results.AddRange(GetItems(path, response.Results));
            } while (continuationToken != null);

            log.LogInformation($"Showing {results.Count} results...");
            foreach (var result in results)
            {
                log.LogInformation($"Type: {result.Type} Path: {result.Path}");
            }
            
            return results;
        }

        private static Func<BlobContinuationToken, Task<BlobResultSegment>> GetBlobsForPath(CloudBlobContainer container,
            IReadOnlyList<string> pathSegments, ILogger log)
        {
            Func<BlobContinuationToken, Task<BlobResultSegment>> getBlobs;
            if (pathSegments.Count == 1)
            {
                getBlobs = container.ListBlobsSegmentedAsync;
                log.LogInformation($"Operating on container {container.Name}.");
            }
            else
            {
                var directory = pathSegments
                    .Skip(2)
                    .Aggregate(container.GetDirectoryReference(pathSegments[1]),
                        (dir, s) => dir.GetDirectoryReference(s));

                getBlobs = directory.ListBlobsSegmentedAsync;
                log.LogInformation($"Operating on directory {directory.Prefix}");
            }

            return getBlobs;
        }

        private static IEnumerable<StorageAccountItem> GetItems(string path, IEnumerable<IListBlobItem> items)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case CloudBlobDirectory d:
                        yield return GetItemForDirectory(path, d);
                        break;
                    case CloudBlockBlob b:
                        var blobItem = GetItemForImage(path, b);
                        if (blobItem != null)
                        {
                            yield return blobItem;
                        }
                        break;
                    default:
                        throw new Exception("Unkonwns");
                }
            }
        }

        private static StorageAccountItem GetItemForDirectory(string path, CloudBlobDirectory d)
        {
            var item = new StorageAccountItem
            {
                Type = "directory",
                Path = $"{path}/{d.Prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Last()}"
            };
            return item;
        }

        private static StorageAccountItem GetItemForImage(string path, CloudBlockBlob b)
        {
            var filename = b.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            var extension = Path.GetExtension(filename);
            if (!".jpg".Equals(extension, StringComparison.InvariantCultureIgnoreCase)) { return null; }
            var item = new StorageAccountItem
            {
                Type = "blob",
                Path = $"{path}/{filename}"
            };
            return item;
        }

        [FunctionName("ProcessImage")]
        public static async Task ProcessImage([ActivityTrigger] string path, ILogger log)
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

        [FunctionName("RetrieveImage")]
        public static async Task<IActionResult> RetrieveImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "RetrieveImage/{*image}")]
            HttpRequestMessage req, 
            [Blob("{image}", FileAccess.Read, Connection = "ImageStorageAccount")]
            Stream s,
            ILogger log)
        {
            // var ms = new MemoryStream();
            // await s.CopyToAsync(ms);
            // ms.Seek(0, SeekOrigin.Begin);

            var magickNetNativeLibraryDirectory = Environment.GetEnvironmentVariable("MAGICK_NET_NATIVE", EnvironmentVariableTarget.Process);
            MagickNET.SetNativeLibraryDirectory(magickNetNativeLibraryDirectory);
            var defines = new JpegWriteDefines
            {
                Extent = 4 * 1024
            };

            var ms = new MemoryStream();
            using (var image = new MagickImage(s))
            {
                image.Settings.SetDefines(defines);
                image.Write(ms);
            }

            ms.Seek(0, SeekOrigin.Begin);

            return new FileStreamResult(ms, "image/jpg");
        }
        
        [FunctionName("ProcessBlobs_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            var instanceId = await starter.StartNewAsync("ProcessBlobs", "testphoto");

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}