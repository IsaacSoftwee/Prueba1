using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using WinForms = System.Windows.Forms;

namespace ImageOptimizerApp;

public partial class MainWindow : Window
{
    private readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };

    private const int DefaultLargeWidth = 600;
    private const double DefaultMediumPercentage = 70;
    private const double DefaultSmallPercentage = 45;
    private const int DefaultLargeQuality = 85;
    private const int DefaultMediumQuality = 80;
    private const int DefaultSmallQuality = 75;

    private string? _selectedFolder;

    public MainWindow()
    {
        InitializeComponent();
        InitializeConfigurationDefaults();
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

        if (!TryReadConfiguration(out var variants, out var validationError))
        {
            MessageBox.Show(validationError, "Configuración no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
            ToggleUi(isProcessing: false);
            return;
        }

        var totalSteps = imageFiles.Count * variants.Count;
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
            await Task.Run(() => ProcessImagesAsync(_selectedFolder!, imageFiles, variants, progress));
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

    private async Task ProcessImagesAsync(string baseFolder, IReadOnlyList<string> imageFiles, IReadOnlyList<ImageVariant> variants, IProgress<ConversionProgress> progress)
    {
        var resultFolder = Path.Combine(baseFolder, "resultado");
        Directory.CreateDirectory(resultFolder);

        var step = 0;
        var totalSteps = imageFiles.Count * variants.Count;

        foreach (var file in imageFiles)
        {
            var fileName = Path.GetFileName(file);

            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(file);

            foreach (var variant in variants)
            {
                var (width, height) = CalculateTargetSize(image, variant.TargetWidth);

                using Image<Rgba32> clone = image.Clone(context =>
                {
                    context.Resize(new ResizeOptions
                    {
                        Mode = ImageSharpResizeMode.Max,
                        Size = new SixLabors.ImageSharp.Size(width, height)
                    });
                });

                var outputFileName = $"{Path.GetFileNameWithoutExtension(file)}_{variant.Name}.webp";
                var outputPath = Path.Combine(resultFolder, outputFileName);

                var encoder = new WebpEncoder
                {
                    Quality = variant.Quality,
                    FileFormat = WebpFileFormatType.Lossy
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

    private void InitializeConfigurationDefaults()
    {
        LargeWidthTextBox.Text = DefaultLargeWidth.ToString(CultureInfo.InvariantCulture);
        MediumPercentageTextBox.Text = DefaultMediumPercentage.ToString(CultureInfo.InvariantCulture);
        SmallPercentageTextBox.Text = DefaultSmallPercentage.ToString(CultureInfo.InvariantCulture);

        LargeQualityTextBox.Text = DefaultLargeQuality.ToString(CultureInfo.InvariantCulture);
        MediumQualityTextBox.Text = DefaultMediumQuality.ToString(CultureInfo.InvariantCulture);
        SmallQualityTextBox.Text = DefaultSmallQuality.ToString(CultureInfo.InvariantCulture);

        UpdateDerivedPreviews();
    }

    private void SizeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateDerivedPreviews();
    }

    private void UpdateDerivedPreviews()
    {
        if (!TryParsePositiveInt(LargeWidthTextBox.Text, out var largeWidth))
        {
            MediumWidthPreviewText.Text = string.Empty;
            SmallWidthPreviewText.Text = string.Empty;
            return;
        }

        MediumWidthPreviewText.Text = TryParsePercentage(MediumPercentageTextBox.Text, out var mediumPercentage)
            ? $"≈ {Math.Max(1, (int)Math.Round(largeWidth * mediumPercentage / 100d))} px"
            : string.Empty;

        SmallWidthPreviewText.Text = TryParsePercentage(SmallPercentageTextBox.Text, out var smallPercentage)
            ? $"≈ {Math.Max(1, (int)Math.Round(largeWidth * smallPercentage / 100d))} px"
            : string.Empty;
    }

    private bool TryReadConfiguration(out List<ImageVariant> variants, out string? errorMessage)
    {
        variants = new List<ImageVariant>();
        errorMessage = null;

        if (!TryParsePositiveInt(LargeWidthTextBox.Text, out var largeWidth))
        {
            errorMessage = "El ancho de la imagen grande debe ser un número entero positivo.";
            return false;
        }

        if (!TryParsePercentage(MediumPercentageTextBox.Text, out var mediumPercentage))
        {
            errorMessage = "El porcentaje de la imagen mediana debe ser un número positivo.";
            return false;
        }

        if (!TryParsePercentage(SmallPercentageTextBox.Text, out var smallPercentage))
        {
            errorMessage = "El porcentaje de la imagen chica debe ser un número positivo.";
            return false;
        }

        if (!TryParseQuality(LargeQualityTextBox.Text, out var largeQuality))
        {
            errorMessage = "La calidad de la imagen grande debe estar entre 0 y 100.";
            return false;
        }

        if (!TryParseQuality(MediumQualityTextBox.Text, out var mediumQuality))
        {
            errorMessage = "La calidad de la imagen mediana debe estar entre 0 y 100.";
            return false;
        }

        if (!TryParseQuality(SmallQualityTextBox.Text, out var smallQuality))
        {
            errorMessage = "La calidad de la imagen chica debe estar entre 0 y 100.";
            return false;
        }

        var mediumWidth = Math.Max(1, (int)Math.Round(largeWidth * mediumPercentage / 100d));
        var smallWidth = Math.Max(1, (int)Math.Round(largeWidth * smallPercentage / 100d));

        variants.Add(new ImageVariant("small", smallWidth, smallQuality));
        variants.Add(new ImageVariant("medium", mediumWidth, mediumQuality));
        variants.Add(new ImageVariant("large", largeWidth, largeQuality));

        return true;
    }

    private static bool TryParsePositiveInt(string? text, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParsePercentage(string? text, out double value)
    {
        if (TryParseDoubleAllowingComma(text, out value) && value > 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseQuality(string? text, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 100)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseDoubleAllowingComma(string? text, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return false;
        }

        var normalized = text.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private record ImageVariant(string Name, int TargetWidth, int Quality);

    private record ConversionProgress(int CompletedSteps, int TotalSteps, string CurrentFileName, string CurrentVariant);
}
