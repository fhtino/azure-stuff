# SACInfo - Storage Account Container Info

---

## ⚠️ **IMPORTANT WARNING** ⚠️

**This entire project is the result of "VIBE CODING" - quick, experimental, and unpolished development.**

**Do NOT use in production environments. Use at your own risk and discretion.**

---

A command-line tool that scans an Azure Storage Account container and generates a detailed directory structure report with file counts and size statistics.

## Description

SACInfo analyzes blob storage containers and produces a TSV (Tab-Separated Values) report containing:
- Directory hierarchy information
- File counts per directory (direct files only)
- Total file counts including subdirectories
- Size statistics in bytes (per directory and total including subdirectories)

## Requirements

- .NET 10
- Azure Storage Account with a container
- SAS URI with read and list permissions for the container

## Configuration

The tool accepts the Container SAS URI in two ways:

### Option 1: Command Line Argument (Recommended)
```bash
SACInfo.exe "https://your-storage-account.blob.core.windows.net/container?sp=r&st=..."
```

### Option 2: Configuration File
Create an `appsettings.json` or `appsettings.secrets.json` file:
```json
{
  "AzureStorage": {
    "ContainerSasUri": "https://your-storage-account.blob.core.windows.net/container?sp=r&st=..."
  }
}
```

**Note:** Command line argument takes priority over configuration file.

## Usage

1. Generate a SAS URI for your Azure Storage container with read and list permissions
2. Run the tool:
   ```bash
   SACInfo.exe "YOUR_CONTAINER_SAS_URI"
   ```
3. The tool will scan the container and create a `directory_stats.tsv` file

## Output

The generated TSV file contains the following columns:
- **DirectoryName**: Name of the directory
- **DirectoryFullPath**: Full path of the directory
- **FilesInDirectory**: Number of files directly in this directory
- **TotalFilesInclSubdirs**: Total files including all subdirectories
- **SizeInDirectoryBytes**: Size of files directly in this directory (bytes)
- **TotalSizeInclSubdirsBytes**: Total size including all subdirectories (bytes)

## Example Output

```
DirectoryName	DirectoryFullPath	FilesInDirectory	TotalFilesInclSubdirs	SizeInDirectoryBytes	TotalSizeInclSubdirsBytes
(root)		10	150	1024000	15360000
images	images	5	50	512000	7680000
docs	docs	3	30	256000	3840000
```

## Notes

- The tool displays progress by showing each directory as it's being processed
- All sizes are reported in bytes
- The root directory is represented as "(root)" in the output
