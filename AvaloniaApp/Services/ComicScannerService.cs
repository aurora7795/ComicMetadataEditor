using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicMetadataEditor;
using AvaloniaApp.ViewModels;

namespace AvaloniaApp.Services;

public class ComicScannerService
{
    public async Task<List<ComicItemViewModel>> ScanDirectoryAsync(
        string directoryPath, 
        bool recursive, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return new List<ComicItemViewModel>();
        }

        return await Task.Run(() =>
        {
            var results = new List<ComicItemViewModel>();
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var editor = new MetadataEditor();

            var files = Directory.GetFiles(directoryPath, "*.*", searchOption)
                .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var model = editor.ReadMetadata(file);
                    var viewModel = new ComicItemViewModel(file, model);
                    results.Add(viewModel);
                }
                catch
                {
                    // Fallback to empty model if file reading fails, allowing recovery/new creation
                    var viewModel = new ComicItemViewModel(file, new ComicInfo());
                    results.Add(viewModel);
                }
            }

            return results;
        }, cancellationToken);
    }
}
