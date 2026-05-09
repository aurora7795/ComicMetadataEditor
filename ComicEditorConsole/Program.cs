// See https://aka.ms/new-console-template for more information
using ComicMetadataEditor;

string directoryPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

var editor = new MetadataEditor();
editor.BulkEditMetadata(directoryPath, comicInfo =>
{
    comicInfo.Manga = "No"; // Set to left-to-right (not Manga style)
    // Add other edits here as needed
});

Console.WriteLine("Metadata updated for CBR files in " + directoryPath);
