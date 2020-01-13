using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace ImageTagging
{
    public static class RetrieveImage
    {
        [FunctionName("RetrieveImage")]
        // ReSharper disable once UnusedMember.Global
        public static async Task<IActionResult> Function(
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
    }
}