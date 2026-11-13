using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using XamlAnimatedGif;

namespace EditorApp
{
    public partial class MainWindow
    {
        private int _activeProgressGifStage;

        private static string GetProgressGifPath(int stage)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EditorApp",
                "ProgressGifs");

            return Path.Combine(folder, $"stage-{stage}.gif");
        }

        private void OpenProgressGifMenu(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }

        private void ChooseProgressGif(object sender, RoutedEventArgs e)
        {
            if (ProcessingProgressBar.IsVisible)
            {
                MessageBox.Show(this, "Дождитесь окончания обработки, чтобы сменить GIF.");
                return;
            }

            var menuItem = (MenuItem)sender;
            if (!int.TryParse(menuItem.Tag as string, out int stage))
                return;

            var picker = new OpenFileDialog {
                Title = menuItem.Header.ToString(),
                Filter = "GIF (*.gif)|*.gif",
                CheckFileExists = true
            };

            if (picker.ShowDialog(this) != true)
                return;

            string destination = GetProgressGifPath(stage);

            try
            {
                using (var stream = File.OpenRead(picker.FileName))
                {
                    var gif = new GifBitmapDecoder(
                        stream,
                        BitmapCreateOptions.None,
                        BitmapCacheOption.OnLoad);

                    if (gif.Frames.Count < 2)
                    {
                        MessageBox.Show(this, "Выберите GIF с анимацией.");
                        return;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (!string.Equals(
                    Path.GetFullPath(picker.FileName),
                    destination,
                    StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(picker.FileName, destination, overwrite: true);
                }

                MessageBox.Show(this, "GIF сохранена. Она появится при обработке.");
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                    or FileFormatException or NotSupportedException)
            {
                MessageBox.Show(this, "Не удалось загрузить GIF. Попробуйте другой файл.");
            }
        }

        private void ProgressGifValueChanged(
            object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateProgressGif();
        }

        private void ProgressGifVisibilityChanged(
            object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateProgressGif();
        }

        private void UpdateProgressGif()
        {
            if (ProcessingProgressBar == null || ProgressGifImage == null)
                return;

            int stage = !ProcessingProgressBar.IsVisible
                ? 0
                : ProcessingProgressBar.Value switch {
                    <= 25 => 1,
                    <= 50 => 2,
                    <= 75 => 3,
                    _ => 4
                };

            if (stage == _activeProgressGifStage)
                return;

            _activeProgressGifStage = stage;
            AnimationBehavior.SetSourceUri(ProgressGifImage, null);
            ProgressGifImage.Visibility = Visibility.Collapsed;

            if (stage == 0)
                return;

            string path = GetProgressGifPath(stage);
            if (!File.Exists(path))
                return;

            AnimationBehavior.SetSourceUri(
                ProgressGifImage, new Uri(path, UriKind.Absolute));

            ProgressGifImage.Visibility = Visibility.Visible;
        }
    }
}