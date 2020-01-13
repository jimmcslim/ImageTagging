using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

namespace ImageTagging
{
    public static class EnumerateBlobs
    {
        [FunctionName("EnumerateBlobs")]
        public static async Task<List<StorageAccountItem>> Function([ActivityTrigger] string path,
            IBinder binder, ILogger log)
        {
            var getBlobs = await GetBlobsForPath(path, binder, log);
            
            var results = new List<StorageAccountItem>();
            BlobContinuationToken continuationToken = null;
            do
            {
                var response = await getBlobs(continuationToken);
                continuationToken = response.ContinuationToken;
                results.AddRange(GetItems(path, response.Results));
            } while (continuationToken != null);

            return results;
        }
        
        private static async Task<Func<BlobContinuationToken, Task<BlobResultSegment>>> GetBlobsForPath(
            string path,
            IBinder binder,
            ILogger log)
        {
            var attr = new BlobAttribute(path) { Connection = "ImageStorageAccount" };
            if (path.Split('/').Length == 1)
            {
                var container = await binder.BindAsync<CloudBlobContainer>(attr);
                log.LogInformation($"Operating on container {container.Name}.");
                return container.ListBlobsSegmentedAsync;
            }
            else
            {
                var directory = await binder.BindAsync<CloudBlobDirectory>(attr);
                log.LogInformation($"Operating on directory {directory.Prefix}");
                return directory.ListBlobsSegmentedAsync;
            }
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
    }
}