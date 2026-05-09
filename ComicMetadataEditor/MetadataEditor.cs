using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace ComicMetadataEditor;

public class MetadataEditor
{
    /// <summary>
    /// Bulk edits the metadata in all CBR files within the specified directory.
    /// </summary>
    /// <param name="directoryPath">The path to the directory containing CBR files.</param>
    /// <param name="editAction">An action to perform on the ComicInfo object for each file.</param>
    public void BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)
    {
        var cbrFiles = Directory.GetFiles(directoryPath, "*.cbr", SearchOption.TopDirectoryOnly);

        foreach (var cbrFile in cbrFiles)
        {
            EditSingleFileMetadata(cbrFile, editAction);
        }
    }

    private void EditSingleFileMetadata(string cbrFilePath, Action<ComicInfo> editAction)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract the archive
            using (Stream stream = File.OpenRead(cbrFilePath))
            using (var reader = ReaderFactory.Open(stream, new ReaderOptions()))
            {
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                    {
                        reader.WriteEntryToDirectory(tempDir, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                    }
                }
            }

            // Find ComicInfo.xml
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            ComicInfo comicInfo;

            if (File.Exists(xmlPath))
            {
                // Deserialize existing XML
                XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
                using (FileStream fs = new FileStream(xmlPath, FileMode.Open))
                {
                    comicInfo = (ComicInfo)serializer.Deserialize(fs)!;
                }
            }
            else
            {
                // Create new if not exists
                comicInfo = new ComicInfo();
            }

            // Apply edits
            editAction(comicInfo);

            // Serialize back to XML
            using (FileStream fs = new FileStream(xmlPath, FileMode.Create))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
                serializer.Serialize(fs, comicInfo);
            }

            // Repack into ZIP (CBZ)
            string tempCbzPath = cbrFilePath + ".tmp";
            string newCbzPath = Path.ChangeExtension(cbrFilePath, ".cbz");
            using (Stream stream = File.OpenWrite(tempCbzPath))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                foreach (var file in Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories))
                {
                    string entryName = GetRelativePath(tempDir, file).Replace('\\', '/');
                    writer.Write(entryName, file);
                }
            }

            // Replace original CBR with new CBZ
            File.Delete(cbrFilePath);
            File.Move(tempCbzPath, newCbzPath);
        }
        finally
        {
            // Clean up temp directory
            Directory.Delete(tempDir, true);
        }
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
        if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentNullException(nameof(relativeTo));
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

        Uri uri1 = new Uri(relativeTo + (relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()) ? "" : Path.DirectorySeparatorChar.ToString()));
        Uri uri2 = new Uri(path);

        Uri relativeUri = uri1.MakeRelativeUri(uri2);

        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }
}
