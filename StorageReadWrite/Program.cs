using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace StorageReadWrite
{

    internal class Program
    {

        private static string _continerSasUri = string.Empty;


        private static List<(string FileName, int SizeMB, string OriginSourceUrl)> testFiles = new()
            {
                { ("A.dat", 27,"https://data.source.coop/harvard-lil/gov-data/file_listing.jsonl.zip" )},
                { ("B.dat", 87,"https://data.source.coop/harvard-lil/gov-data/collections/data_gov/1995-street-tree-census/v1.zip") },
                { ("C.dat", 112,"https://data.source.coop/harvard-lil/gov-data/collections/data_gov/1980-annual-report/v1.zip") },
                // { ("D.dat",1000, "https://data.source.coop/harvard-lil/gov-data/metadata.jsonl.zip" )},   // too big for single call to  SyncCopyFromUri Error: "The source request body for synchronous copy is too large and exceeds the maximum permissible limit (256MB)."
            };



        public static async Task Main(string[] args)
        {
            // Just some experiments with the Azure Blob Storage SDK to understand how it works under the hood,
            // especially around buffering and performance implications of different read/write patterns.

            // Read configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false)
                .Build();
            _continerSasUri = configuration["ContainerSasUri"]!;

            CreateTestFiles();

            if (false) await SimpleStreamRead();
            if (false) await JumpAroundUsingSeek();
            if (false) await SimpleStreamWrite();
            if (false) await ReadAndWrite();

            // TODO:
            //  - play with AppendBlob
            //  - play with PageBlob
            //  - SyncCopyFromUri with big files (> 256 MB)
            //  - ...
        }



        private static async Task SimpleStreamRead()
        {
            /*
            If the internal BlobClient is empty, the first read will trigger a fetch of the first X MB chunk from Azure Blob Storage. X it can be configured. 
            Subsequent reads will be served from the internal buffer until it is exhausted, at which point another fetch will occur. 
            The output will show the total bytes read, the size of each read operation, and the time taken for each read:
                  4706304 : 512000 : 300
                  5218304 : 512000 : 0
                  5730304 : 512000 : 0
                  6242304 : 512000 : 0
                  6291456 : 49152 : 0
            */

            var containerClient = new BlobContainerClient(new Uri(_continerSasUri));

            BlobClient blobClient = containerClient.GetBlobClient("A.dat");
            //BlockBlobClient blobClient = containerClient.GetBlockBlobClient("A.dat");

            using var blocStream = blobClient.OpenRead(bufferSize: 2 * 1024 * 1024);  // The library default buffer size is 4 MB.
            using var localFileStream = File.Create("A.dat.local");
            var chunk = new byte[500 * 1024];
            int totalBytes = 0;

            var sw = Stopwatch.StartNew();

            while (true)
            {
                sw.Restart();
                int size = blocStream.Read(chunk, 0, chunk.Length);
                totalBytes += size;
                Console.WriteLine($"{totalBytes} : {size} : {sw.ElapsedMilliseconds}");
                if (size == 0)
                {
                    Console.WriteLine("End of stream reached.");
                    break;
                }

                localFileStream.Write(chunk, 0, size);
            }
        }


        private static async Task JumpAroundUsingSeek()
        {
            /*
            Because we read just 2 KB of data, jumping around the file using Seek, it important to use a small buffer size in OpenRead, 
            otherwise the library will fetch a large chunk of data (default 4 MB) everytime the seek position moves outside of the current buffer range, 
            which can lead to significant overhead and latency.
            */

            var rnd = new Random();
            var sw = Stopwatch.StartNew();

            var containerClient = new BlobContainerClient(new Uri(_continerSasUri));
            BlobClient blobClient = containerClient.GetBlobClient("A.dat");
            using var blobStream = blobClient.OpenRead(bufferSize: 2 * 1024);

            var chunk = new byte[2 * 1024];

            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                int position = rnd.Next(10 * 1024 * 1024);
                blobStream.Seek(position, SeekOrigin.Begin);
                int bytesRead = await blobStream.ReadAsync(chunk, 0, chunk.Length);
                Console.WriteLine($"Position: {position}, Bytes Read: {bytesRead}, Time: {sw.ElapsedMilliseconds} ms");
            }
        }


        private static async Task SimpleStreamWrite()
        {
            /*
            As with reading, the operations are locally buffered. Only when the buffer is full, the data is flushed to Azure Blob Storage.
            In the example below, we write 512 KB + 1 byte of data to the stream in each iteration, and we set the buffer size to 2 MB:            
                0 : 0
                1 : 0
                2 : 0
                3 : 929
            */
            var containerClient = new BlobContainerClient(new Uri(_continerSasUri));

            BlobClient blobClient = containerClient.GetBlobClient("myfile1.dat");
            blobClient.DeleteIfExists();

            var chunk = new byte[1 + 512 * 1024];
            new Random().NextBytes(chunk);

            using var blobStream = blobClient.OpenWrite(overwrite: true, new BlobOpenWriteOptions() { BufferSize = 2 * 1024 * 1024 });

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                blobStream.Write(chunk);
                Console.WriteLine($"{i} : {sw.ElapsedMilliseconds}");
            }
        }


        private static async Task ReadAndWrite()
        {
            // Read from on blob and write to another blob, using streams. This is useful when you want to process the data on the fly without storing it locally.
            // The example also show how to calculate an MD5 hash of the data while processing and then set it as the content hash of the destination blob.

            /*
            1048576 : 635 ms / 3 ms
            2097152 : 0 ms / 0 ms
            3145728 : 0 ms / 0 ms
            4194304 : 0 ms / 0 ms
            5242880 : 572 ms / 2015 ms
            6291456 : 0 ms / 0 ms
            ....
            24117248 : 0 ms / 0 ms
            25165824 : 0 ms / 0 ms
            26214400 : 368 ms / 1738 ms
            27262976 : 0 ms / 1 ms
            27816364 : 0 ms / 0 ms            
            Last write: 1182 ms
             */


            var swRead = Stopwatch.StartNew();
            var swWrite = Stopwatch.StartNew();

            var containerClient = new BlobContainerClient(new Uri(_continerSasUri));

            BlobClient sourceBlobClient = containerClient.GetBlobClient("A.dat");
            BlobClient destBlobClient = containerClient.GetBlobClient("myFile3.dat");

            using (var md5 = MD5.Create())
            using (var sourceStream = await sourceBlobClient.OpenReadAsync())
            {
                using (var destStream = await destBlobClient.OpenWriteAsync(overwrite: true, new BlobOpenWriteOptions() { BufferSize = 4 * 1024 * 1024 }))
                {
                    var buffer = new byte[1024 * 1024];

                    int totalBytes = 0;
                    while (true)
                    {
                        // Read from source blob
                        swRead.Restart();
                        int bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length);
                        totalBytes += bytesRead;
                        swRead.Stop();
                        if (bytesRead == 0) { break; }

                        // Do some processing on the buffer 
                        for (int i = 0; i < bytesRead; i++) { buffer[i] = (byte)(buffer[i] ^ 0xFF); /* Just an example of processing: invert the bytes */                        }

                        // Update MD5 hash with processed data
                        md5.TransformBlock(buffer, 0, bytesRead, null, 0);

                        // Write to destination blob
                        swWrite.Restart();
                        await destStream.WriteAsync(buffer, 0, bytesRead);
                        swWrite.Stop();

                        Console.WriteLine($"{totalBytes} : {swRead.ElapsedMilliseconds} ms / {swWrite.ElapsedMilliseconds} ms");
                    }

                    swWrite.Restart();
                }
                Console.WriteLine($"Last write: {swWrite.ElapsedMilliseconds} ms");

                // Finalize MD5 hash calculation
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                byte[] hash = md5.Hash!;

                // Set the MD5 hash on the blob
                await destBlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentHash = hash });

                Console.WriteLine($"MD5 hash set: {Convert.ToBase64String(hash)}");
            }
        }



        private static void CreateTestFiles()
        {
            Console.WriteLine("** Creating test files **");

            BlobContainerClient containerClient = new BlobContainerClient(new Uri(_continerSasUri));

            foreach (var sourceFile in testFiles)
            {
                var blobClient = containerClient.GetBlobClient(sourceFile.FileName);

                if (!blobClient.Exists())
                {
                    Console.WriteLine($"Processing {sourceFile.FileName} : Copying from {sourceFile.OriginSourceUrl} to {blobClient.Uri}");
                    blobClient.SyncCopyFromUri(new Uri(sourceFile.OriginSourceUrl));
                }

                Console.WriteLine($"{sourceFile.FileName} : {blobClient.GetProperties().Value.ContentLength}");
            }
        }

    }

}
