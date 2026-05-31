using System;
using System.IO;
using ComicMetadataEditor;

string directoryPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

if (!Directory.Exists(directoryPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: The directory '{directoryPath}' does not exist or is not accessible.");
    Console.ResetColor();
    Environment.Exit(1);
}

Console.WriteLine($"Scanning directory: {directoryPath}");
var editor = new MetadataEditor();

BulkEditReport report;
try
{
    report = editor.BulkEditMetadata(directoryPath, comicInfo =>
    {
        comicInfo.Manga = "No"; // Set to left-to-right (not Manga style)
        // Add other edits here as needed
    });
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Critical failure during bulk metadata editing: {ex.Message}");
    Console.ResetColor();
    Environment.Exit(1);
    return;
}

Console.WriteLine($"\nFound {report.TotalFound} comic archives (.cbr / .cbz).\n");

if (report.TotalFound == 0)
{
    Console.WriteLine("No supported comic archives found.");
    return;
}

foreach (var success in report.Successes)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("[SUCCESS] ");
    Console.ResetColor();
    Console.WriteLine(Path.GetFileName(success));
}

foreach (var failure in report.Failures)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("[FAILURE] ");
    Console.ResetColor();
    Console.WriteLine($"{Path.GetFileName(failure.Path)} - Error: {failure.Exception}");
}

Console.WriteLine("\n==========================================");
if (report.Failures.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
}
Console.WriteLine($"Summary: Total Found: {report.TotalFound} | Succeeded: {report.Successes.Count} | Failed: {report.Failures.Count}");
Console.ResetColor();
Console.WriteLine("==========================================\n");
