using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace CatMin
{
    public partial class VersionSettingsWindow : FluentWindow
    {
        private string _versionName;
        private string _minecraftPath;

        public VersionSettingsWindow(string versionName, string minecraftPath)
        {
            InitializeComponent();
            _versionName = versionName;
            _minecraftPath = minecraftPath;

            TitleText.Text = $"版本设置 - {versionName}";
            VersionNameText.Text = versionName;
            VersionTypeText.Text = "Minecraft 游戏版本";

            try
            {
                if (versionName.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                    VersionIcon.Source = new BitmapImage(new Uri("pack://application:,,,/fabric.png"));
                else if (versionName.Contains("forge", StringComparison.OrdinalIgnoreCase))
                    VersionIcon.Source = new BitmapImage(new Uri("pack://application:,,,/forge.png"));
                else
                    VersionIcon.Source = new BitmapImage(new Uri("pack://application:,,,/vanilla.png"));
            }
            catch { }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(_minecraftPath, "versions", _versionName);
            if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"確定要刪除版本 {_versionName} 嗎？", "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    string path = Path.Combine(_minecraftPath, "versions", _versionName);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    this.Close();
                }
                catch (Exception ex) { MessageBox.Show($"刪除失敗: {ex.Message}"); }
            }
        }
    }
}