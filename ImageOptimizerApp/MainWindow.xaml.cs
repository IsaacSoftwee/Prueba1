using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WinForms = System.Windows.Forms;

namespace ImageOptimizerApp;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<ImageVariant> _variants = new List<ImageVariant>
    {
        new("chico", 400, 75),
        new("mediano", 800, 80),
        new("grande", 1200, 85)
    };

    private readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };

    private string? _selectedFolder;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Selecciona la carpeta origen con las imágenes",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK && Directory.Exists(dialog.SelectedPath))
        {
            _selectedFolder = dialog.SelectedPath;
            FolderPathTextBox.Text = _selectedFolder;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFolder) || !Directory.Exists(_selectedFolder))
        {
            MessageBox.Show("Selecciona una carpeta de origen válida.", "Carpeta no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var imageFiles = Directory
            .EnumerateFiles(_selectedFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => _supportedExtensions.Contains(Path.GetExtension(path) ?? string.Empty))
            .OrderBy(path => path)
            .ToList();

        if (imageFiles.Count == 0)
        {
            MessageBox.Show("La carpeta seleccionada no contiene imágenes compatibles.", "Sin imágenes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ToggleUi(isProcessing: true);

        var totalSteps = imageFiles.Count * _variants.Count;
        ConversionProgressBar.Minimum = 0;
        ConversionProgressBar.Maximum = totalSteps;
        ConversionProgressBar.Value = 0;
        PercentageText.Text = "0%";
        StatusText.Text = "Preparando conversión...";

        var progress = new Progress<ConversionProgress>(progressInfo =>
        {
            ConversionProgressBar.Value = progressInfo.CompletedSteps;
            var percentage = progressInfo.TotalSteps == 0
                ? 0
                : (double)progressInfo.CompletedSteps / progressInfo.TotalSteps * 100d;

            PercentageText.Text = $"{percentage:0}%";
            StatusText.Text = $"Convirtiendo {progressInfo.CurrentFileName} ({progressInfo.CurrentVariant})";
        });

        try
        {
            await Task.Run(() => ProcessImagesAsync(_selectedFolder!, imageFiles, progress));
            ConversionProgressBar.Value = ConversionProgressBar.Maximum;
            PercentageText.Text = "100%";
            StatusText.Text = "Conversión finalizada. Encontrarás los archivos en la carpeta 'resultado'.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ocurrió un error durante la conversión.";
            MessageBox.Show($"Ocurrió un error al convertir las imágenes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleUi(isProcessing: false);
        }
    }

    private async Task ProcessImagesAsync(string baseFolder, IReadOnlyList<string> imageFiles, IProgress<ConversionProgress> progress)
    {
        var resultFolder = Path.Combine(baseFolder, "resultado");
        Directory.CreateDirectory(resultFolder);

        var step = 0;
        var totalSteps = imageFiles.Count * _variants.Count;

        foreach (var file in imageFiles)
        {
            var fileName = Path.GetFileName(file);

            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(file);

            foreach (var variant in _variants)
            {
                var (width, height) = CalculateTargetSize(image, variant.TargetWidth);

                using Image<Rgba32> clone = image.Clone(context =>
                {
                    context.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new SixLabors.ImageSharp.Size(width, height)
                    });
                });

                var outputFileName = $"{Path.GetFileNameWithoutExtension(file)}_{variant.Name}.webp";
                var outputPath = Path.Combine(resultFolder, outputFileName);

                var encoder = new WebpEncoder
                {
                    Quality = variant.Quality,
                    FileFormat = WebpFileFormatType.Auto
                };

                await clone.SaveAsync(outputPath, encoder);

                step++;
                progress.Report(new ConversionProgress(step, totalSteps, fileName, variant.Name));
            }
        }
    }

    private static (int width, int height) CalculateTargetSize(Image<Rgba32> image, int targetWidth)
    {
        if (image.Width <= targetWidth)
        {
            return (image.Width, image.Height);
        }

        var ratio = (double)targetWidth / image.Width;
        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * ratio));
        return (targetWidth, targetHeight);
    }

    private void ToggleUi(bool isProcessing)
    {
        if (isProcessing)
        {
            LoaderText.Text = "Procesando imágenes...";
            LoaderPanel.Visibility = Visibility.Visible;
        }
        else if (ConversionProgressBar.Maximum > 0 && Math.Abs(ConversionProgressBar.Value - ConversionProgressBar.Maximum) < 0.001)
        {
            LoaderText.Text = "Proceso finalizado";
            LoaderPanel.Visibility = Visibility.Visible;
        }
        else
        {
            LoaderPanel.Visibility = Visibility.Collapsed;
        }

        BrowseButton.IsEnabled = !isProcessing;
        StartButton.IsEnabled = !isProcessing;
    }

    private record ImageVariant(string Name, int TargetWidth, int Quality);

    private record ConversionProgress(int CompletedSteps, int TotalSteps, string CurrentFileName, string CurrentVariant);
}
