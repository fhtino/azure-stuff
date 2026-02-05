using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SACInfo
{

    // ---------------------------------------------------------------
    // ---------------------------------------------------------------
    // All 'vibe coding' here. Don't blame too much.  :)
    // ---------------------------------------------------------------
    // ---------------------------------------------------------------

    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false)
                .Build();

            // Read Azure Storage Container SAS URI from command line or configuration
            string? storageAccountContainerSASUri = null;
            
            // Check command line arguments first (higher priority)
            if (args.Length > 0)
            {
                storageAccountContainerSASUri = args[0];
                Console.WriteLine("Using ContainerSasUri from command line argument");
            }
            else
            {
                // Fall back to configuration file
                storageAccountContainerSASUri = configuration["AzureStorage:ContainerSasUri"];
                if (!string.IsNullOrEmpty(storageAccountContainerSASUri))
                {
                    Console.WriteLine("Using ContainerSasUri from configuration file");
                }
            }

            if (string.IsNullOrEmpty(storageAccountContainerSASUri))
            {
                throw new InvalidOperationException("ContainerSasUri not found. Provide it as a command line argument or in the configuration file.");
            }

            var containerClient = new BlobContainerClient(new Uri(storageAccountContainerSASUri));

            Console.WriteLine("Scanning container...");
            var directoryStats = await BuildDirectoryStructure(containerClient);

            string outputFile = "directory_stats.tsv";
            await WriteToTsv(directoryStats, outputFile);

            Console.WriteLine($"TSV file created: {outputFile}");
        }

        static async Task<Dictionary<string, DirectoryInfo>> BuildDirectoryStructure(BlobContainerClient containerClient)
        {
            var directories = new Dictionary<string, DirectoryInfo>();
            
            // Initialize root directory
            directories[""] = new DirectoryInfo { Name = "(root)", FullPath = "" };

            await foreach (BlobItem blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: null, cancellationToken: default))
            {
                string blobPath = blob.Name;
                long blobSize = blob.Properties.ContentLength ?? 0;

                // Extract all parent directories from the blob path
                string currentPath = "";
                string[] pathParts = blobPath.Split('/');
                
                // Process each directory level (excluding the file name)
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string parentPath = currentPath;
                    currentPath = string.IsNullOrEmpty(currentPath) 
                        ? pathParts[i] 
                        : currentPath + "/" + pathParts[i];

                    if (!directories.ContainsKey(currentPath))
                    {
                        directories[currentPath] = new DirectoryInfo
                        {
                            Name = pathParts[i],
                            FullPath = currentPath
                        };
                        Console.WriteLine($"Processing directory: {currentPath}");
                    }
                }

                // Determine the immediate parent directory of the file
                string immediateParentPath = "";
                if (pathParts.Length > 1)
                {
                    // File is in a subdirectory
                    for (int i = 0; i < pathParts.Length - 1; i++)
                    {
                        if (i == 0)
                            immediateParentPath = pathParts[i];
                        else
                            immediateParentPath += "/" + pathParts[i];
                    }
                }
                // else: file is in root (immediateParentPath stays "")

                // Add file count and size to immediate parent directory only
                directories[immediateParentPath].FilesInDirectory++;
                directories[immediateParentPath].SizeInDirectory += blobSize;
            }

            // Calculate recursive statistics properly
            CalculateRecursiveStats(directories);

            return directories;
        }

        static void CalculateRecursiveStats(Dictionary<string, DirectoryInfo> directories)
        {
            // Reset recursive counts to prepare for proper calculation
            foreach (var dir in directories.Values)
            {
                dir.TotalFilesIncludingSubdirs = dir.FilesInDirectory;
                dir.TotalSizeIncludingSubdirs = dir.SizeInDirectory;
            }

            // Sort directories by depth (deepest first) to calculate bottom-up
            var sortedDirs = directories.OrderByDescending(d => d.Key.Count(c => c == '/')).ToList();

            foreach (var kvp in sortedDirs)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue; // Skip root for now

                // Find parent directory
                int lastSlashIndex = kvp.Key.LastIndexOf('/');
                string parentPath = lastSlashIndex >= 0 ? kvp.Key.Substring(0, lastSlashIndex) : "";

                if (directories.ContainsKey(parentPath))
                {
                    // Add this directory's total stats to parent
                    directories[parentPath].TotalFilesIncludingSubdirs += kvp.Value.TotalFilesIncludingSubdirs;
                    directories[parentPath].TotalSizeIncludingSubdirs += kvp.Value.TotalSizeIncludingSubdirs;
                }
            }
        }

        static async Task WriteToTsv(Dictionary<string, DirectoryInfo> directories, string outputFile)
        {
            using var writer = new StreamWriter(outputFile);
            
            // Write header
            await writer.WriteLineAsync("DirectoryName\tDirectoryFullPath\tFilesInDirectory\tTotalFilesInclSubdirs\tSizeInDirectoryBytes\tTotalSizeInclSubdirsBytes");

            // Sort directories by path for better readability
            var sortedDirs = directories.OrderBy(d => d.Key).ToList();

            foreach (var kvp in sortedDirs)
            {
                var dir = kvp.Value;
                await writer.WriteLineAsync($"{dir.Name}\t{dir.FullPath}\t{dir.FilesInDirectory}\t{dir.TotalFilesIncludingSubdirs}\t{dir.SizeInDirectory}\t{dir.TotalSizeIncludingSubdirs}");
            }
        }
    }

    class DirectoryInfo
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public int FilesInDirectory { get; set; }
        public int TotalFilesIncludingSubdirs { get; set; }
        public long SizeInDirectory { get; set; }
        public long TotalSizeIncludingSubdirs { get; set; }
    }
}
