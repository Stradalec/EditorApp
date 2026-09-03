using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace EditorApp.services.document
{
    internal class DocumentService : IDocumentService
    {
        private readonly DocumentListExtractService _extractService;
        private readonly DocumentListDiffService _diffService;
        private readonly DocumentWriterService _writerService;
        public DocumentService()
        {
            _extractService = new DocumentListExtractService();
            _diffService = new DocumentListDiffService();
            _writerService = new DocumentWriterService(_diffService);
        }
        private List<string> _oldLiteratureList = new List<string>();
        public async Task<string> ExtractBibliographyAsync(string filePath)
        {
            var result = await _extractService.ExtractBibliography(filePath);
            _oldLiteratureList = result.Items;
            return result.Text;
        }

        public async Task<string> SaveAsProcessedAsync(string originalPath, string bibliographyText, string flag, string suffix = "_Обработано")
        {
            if (!File.Exists(originalPath)) 
            {
                throw new FileNotFoundException("Файл не найден", originalPath);
            }
                

            string directory = Path.GetDirectoryName(originalPath)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            string timestamp = DateTime.Now.ToString("HHmmss");
            string outputPath = originalPath;
            if (!originalPath.Contains("_Обработано"))
            {
                outputPath = Path.Combine(directory, $"{fileNameWithoutExt}{suffix}_{timestamp}{extension}");
            }

            if (!string.Equals(originalPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => File.Copy(originalPath, outputPath, overwrite: true));
            }

            if (flag == "list")
            {
                await Task.Run(() => _writerService.AppendBibliographyToDocument(outputPath, bibliographyText, _oldLiteratureList));
            }
                

            return outputPath;
        }
        public Task OpenAsProcessed(string originalPath)
        {
            OpenWithDefaultApp(originalPath);
            return Task.CompletedTask;
        }
        
        private void OpenWithDefaultApp(string path)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(path) {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Не удалось открыть файл: {ex.Message}", ex);
            }
        }

    }
}
