using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace AppendBlobSlowness
{

    internal class Program
    {

        public static void Main(string[] args)
        {
            // Read the full article at https://www.fhtino.it/notes/azure/storageaccount.html

            var rnd = new Random();
            int blobSize = 10 * 1000 * 1000;
            int[] numOfChunkList = new int[] { 1, 10, 100, 1000, 10000 };

            // Configuration & setup
            IConfiguration globalConfig = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("../../../../../AppendBlobSlowness.secrets.json"), optional: false, reloadOnChange: false).Build();
            string connectionString = globalConfig["connectionString"]!;
            string containerName = globalConfig["containerName"]!;
            bool overwriteblobs = bool.Parse(globalConfig["overwriteblobs"] ?? "false");
            var blobContainerClient = new BlobContainerClient(connectionString, containerName);

            // Create append blobs
            var appendBlobFileNames = new List<string>();
            foreach (var numOfChunks in numOfChunkList)
            {
                int chunkSize = blobSize / numOfChunks;
                byte[] chunkBody = new byte[chunkSize];
                string appendBlobName = $"{numOfChunks}_{chunkSize}.append.dat";
                appendBlobFileNames.Add(appendBlobName);

                Console.Write($"Uploading append blob {appendBlobName} : ");
                var appendBlobClient = blobContainerClient.GetAppendBlobClient(appendBlobName);
                if (!overwriteblobs && appendBlobClient.Exists())
                {
                    Console.WriteLine(" already exists, skipping.");
                    continue;
                }

                appendBlobClient.DeleteIfExists();
                appendBlobClient.Create();
                for (int i = 0; i < numOfChunks; i++)
                {
                    Console.Write($"{i} ");
                    rnd.NextBytes(chunkBody);
                    using (var stream = new System.IO.MemoryStream(chunkBody))
                    {
                        appendBlobClient.AppendBlock(stream);
                    }
                }
                Console.WriteLine();
            }

            // Create block blobs from append blobs
            Console.WriteLine("------------------------------------------");
            var blockBlobFileNames = new List<string>();
            foreach (var appendBlobName in appendBlobFileNames)
            {
                string blockBlobName = appendBlobName.Replace(".append.", ".block.");
                blockBlobFileNames.Add(blockBlobName);
                Console.Write($"Create block blob {blockBlobName} from append blob : ");
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                var appendBlobClient = blobContainerClient.GetAppendBlobClient(appendBlobName);
                int numberOfInternalBlocks= appendBlobClient.GetProperties().Value.BlobCommittedBlockCount;  // just for information
                var sourceBlobSASURI = appendBlobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10));
                var blockBlobClient = blobContainerClient.GetBlockBlobClient(blockBlobName);
                blockBlobClient.DeleteIfExists();
                blockBlobClient.SyncUploadFromUri(sourceBlobSASURI, new BlobSyncUploadFromUriOptions { CopySourceBlobProperties = true, });
                Console.WriteLine($" {sw.ElapsedMilliseconds / 1000.0:N3} s");
            }

            // Download append and block blobs
            Console.WriteLine("------------------------------------------");
            var allBlobFileNames = appendBlobFileNames.Concat(blockBlobFileNames).ToList();
            foreach (var blobFileName in allBlobFileNames)
            {
                var bco = new BlobClientOptions();
                bco.Diagnostics.IsLoggingEnabled = false;
                var blobClient = new BlobClient(connectionString, containerName, blobFileName, bco);
                Console.Write($"Downloading {blobFileName} : ");
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                var downMS = new System.IO.MemoryStream();
                var resp = blobClient.DownloadTo(downMS);
                Console.WriteLine($" {sw.ElapsedMilliseconds / 1000.0:N3} s");
            }

        }

    }

}
