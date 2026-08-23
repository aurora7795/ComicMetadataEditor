using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Services;

public record struct ScanProgressReport(
    int Processed,
    int Total,
    string? CurrentFileName = null,
    long? CurrentFileSizeBytes = null,
    bool IsUnseekableStream = false);

public class ComicScannerService
{
    public Task<List<ComicItemViewModel>> ScanDirectoryAsync(
        string directoryPath, 
        bool recursive, 
        CancellationToken cancellationToken,
        IProgress<(int Processed, int Total)>? progress)
    {
        IProgress<ScanProgressReport>? mappedProgress = progress == null 
            ? null 
            : new Progress<ScanProgressReport>(r => progress.Report((r.Processed, r.Total)));
        return ScanDirectoryAsync(directoryPath, recursive, cancellationToken, mappedProgress);
    }

    public async Task<List<ComicItemViewModel>> ScanDirectoryAsync(
        string directoryPath, 
        bool recursive, 
        CancellationToken cancellationToken,
        IProgress<ScanProgressReport>? progress = null)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath) || cancellationToken.IsCancellationRequested)
        {
            return new List<ComicItemViewModel>();
        }

        try
        {
            return await Task.Run(async () =>
            {
                var totalSw = System.Diagnostics.Stopwatch.StartNew();
                Core.Logging.AppLogger.LogDebug($"[Scanner] Starting scan of '{directoryPath}' (Recursive: {recursive})...");

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var editor = new MetadataEditor();

                var enumSw = System.Diagnostics.Stopwatch.StartNew();
                var files = Directory.EnumerateFiles(directoryPath, "*.*", searchOption)
                    .Where(MetadataEditor.IsSupportedComicFile)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                enumSw.Stop();

                Core.Logging.AppLogger.LogDebug($"[Scanner] Directory enumeration discovered {files.Count} comic archive files in {enumSw.ElapsedMilliseconds}ms.");

                if (files.Count == 0)
                {
                    return new List<ComicItemViewModel>();
                }

                var indexedResults = new System.Collections.Concurrent.ConcurrentDictionary<int, ComicItemViewModel>();
                int maxConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 8);
                Core.Logging.AppLogger.LogDebug($"[Scanner] Launching parallel processing with {maxConcurrency} workers...");

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = cancellationToken
                };

                int processedCount = 0;
                int unseekableDetected = 0;
                progress?.Report(new ScanProgressReport(0, files.Count));

                try
                {
                    await Parallel.ForEachAsync(
                        Enumerable.Range(0, files.Count),
                        parallelOptions,
                        async (index, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            string file = files[index];
                            string fileName = Path.GetFileName(file);
                            long fileSizeBytes = 0;
                            try
                            {
                                var fi = new FileInfo(file);
                                if (fi.Exists) fileSizeBytes = fi.Length;
                            }
                            catch { }

                            progress?.Report(new ScanProgressReport(
                                Volatile.Read(ref processedCount),
                                files.Count,
                                fileName,
                                fileSizeBytes,
                                Volatile.Read(ref unseekableDetected) > 0));

                            var fileSw = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                var model = editor.ReadMetadata(file, out bool hasEmbeddedXml, out bool usedSequential, ct);
                                if (usedSequential)
                                {
                                    Interlocked.Exchange(ref unseekableDetected, 1);
                                }

                                var viewModel = new ComicItemViewModel(file, model, hasEmbeddedXml);
                                indexedResults[index] = viewModel;
                                Core.Logging.AppLogger.LogDebug($"[Scanner] [{index + 1}/{files.Count}] Parsed '{fileName}' in {fileSw.ElapsedMilliseconds}ms (Sequential fallback: {usedSequential}, HasXml: {hasEmbeddedXml}).");
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                var viewModel = new ComicItemViewModel(file, new ComicInfo(), hasEmbeddedXml: false)
                                {
                                    HasReadError = true,
                                    ReadErrorMessage = ex.Message
                                };
                                indexedResults[index] = viewModel;
                                Core.Logging.AppLogger.LogWarning($"[Scanner] [{index + 1}/{files.Count}] Failed to parse '{fileName}' in {fileSw.ElapsedMilliseconds}ms: {ex.Message}");
                            }

                            int current = Interlocked.Increment(ref processedCount);
                            progress?.Report(new ScanProgressReport(
                                current,
                                files.Count,
                                fileName,
                                fileSizeBytes,
                                Volatile.Read(ref unseekableDetected) > 0));
                            await Task.Yield();
                        });
                }
                catch (OperationCanceledException)
                {
                    Core.Logging.AppLogger.LogDebug($"[Scanner] Directory scan cancelled by user after {processedCount}/{files.Count} items.");
                }

                totalSw.Stop();
                var finalResults = Enumerable.Range(0, files.Count)
                    .Where(indexedResults.ContainsKey)
                    .Select(i => indexedResults[i])
                    .ToList();

                Core.Logging.AppLogger.LogDebug($"[Scanner] Scan completed: Loaded {finalResults.Count}/{files.Count} comics in {totalSw.ElapsedMilliseconds}ms total.");
                return finalResults;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new List<ComicItemViewModel>();
        }
    }
}
