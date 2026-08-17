using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Services;

public class ComicScannerService
{
    public async Task<List<ComicItemViewModel>> ScanDirectoryAsync(
        string directoryPath, 
        bool recursive, 
        CancellationToken cancellationToken,
        IProgress<(int Processed, int Total)>? progress = null)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath) || cancellationToken.IsCancellationRequested)
        {
            return new List<ComicItemViewModel>();
        }

        try
        {
            return await Task.Run(async () =>
            {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var editor = new MetadataEditor();

            var files = Directory.GetFiles(directoryPath, "*.*", searchOption)
                .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return new List<ComicItemViewModel>();
            }

            var indexedResults = new System.Collections.Concurrent.ConcurrentDictionary<int, ComicItemViewModel>();
            int maxConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 8);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            };

            int processedCount = 0;
            progress?.Report((0, files.Count));

            try
            {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, files.Count),
                    parallelOptions,
                    async (index, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        string file = files[index];
                        try
                        {
                            var model = editor.ReadMetadata(file);
                            var viewModel = new ComicItemViewModel(file, model);
                            indexedResults[index] = viewModel;
                        }
                        catch (Exception ex)
                        {
                            var viewModel = new ComicItemViewModel(file, new ComicInfo())
                            {
                                HasReadError = true,
                                ReadErrorMessage = ex.Message
                            };
                            indexedResults[index] = viewModel;
                        }

                        int current = Interlocked.Increment(ref processedCount);
                        progress?.Report((current, files.Count));
                        await Task.Yield();
                    });
            }
            catch (OperationCanceledException)
            {
                // Gracefully return whatever items were parsed prior to cancellation
            }

            return Enumerable.Range(0, files.Count)
                .Where(indexedResults.ContainsKey)
                .Select(i => indexedResults[i])
                .ToList();
        }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new List<ComicItemViewModel>();
        }
    }
}
