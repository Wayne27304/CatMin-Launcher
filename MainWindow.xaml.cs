using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using DiscordRPC;
using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Diagnostics;

using Button = Wpf.Ui.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;
using Image = System.Windows.Controls.Image;
using CheckBox = System.Windows.Controls.CheckBox;

namespace CatMin
{
    public partial class MainWindow : FluentWindow
    {
        private MinecraftLauncher _launcher = null!;
        private MSession _currentSession = null!;
        private DiscordRpcClient? _discordClient;
        private string _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CatMin", "launcher_config.json");
        private static readonly HttpClient _httpClient = new HttpClient();

        private bool _isInstalling = false;
        private bool _isLaunching = false;

        public class AppConfig
        {
            public string Username { get; set; } = "Player";
            public int MaxMemory { get; set; } = 4096;
            public string JavaPath { get; set; } = "";
            public string JvmArgs { get; set; } = "";
            public bool EnableDiscordRpc { get; set; } = true;
            public bool FullScreen { get; set; } = false;
            public bool VersionIsolation { get; set; } = true;
            public System.Collections.Generic.List<string> OfflineAccounts { get; set; } = new() { "Player" };
            public string WallpaperPath { get; set; } = "";
            public string MinecraftDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        }

        private AppConfig _config = new AppConfig();

        public MainWindow()
        {
            InitializeComponent();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CatMinLauncher/2.1.0");

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

            LoadConfig();
            ReloadMinecraftPath();
            
            ApplyWallpaper();
            InitDiscordRpc();
            UpdateMemoryInfo();
            LoadSavedAccounts();
        }

        private void ShowPanel(FrameworkElement panelToShow)
        {
            HomePanel.Visibility = Visibility.Collapsed;
            VersionManagerPanel.Visibility = Visibility.Collapsed;
            DownloadPanel.Visibility = Visibility.Collapsed;
            ModManagerPanel.Visibility = Visibility.Collapsed;
            ModsPanel.Visibility = Visibility.Collapsed;
            ServerPanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Collapsed;
            LogPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            LaunchingPanel.Visibility = Visibility.Collapsed;

            panelToShow.Visibility = Visibility.Visible;
            if (FindResource("PageTransition") is Storyboard anim) anim.Begin(panelToShow);
        }

        // ================= 全新：獨立啟動介面邏輯 =================
        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (_isLaunching) return;
            if (VersionCombo.SelectedItem == null || VersionCombo.SelectedItem.ToString() == "未安装任何版本") 
            {
                MessageBox.Show("請先選擇一個遊戲版本，或去下載中心下載！", "提示");
                return;
            }
            
            string version = VersionCombo.SelectedItem.ToString()!;

            if (!Directory.Exists(_config.MinecraftDir))
            {
                MessageBox.Show($"找不到遊戲目錄：\n{_config.MinecraftDir}\n\n請至設定更改目錄。", "錯誤");
                return;
            }

            _isLaunching = true;
            
            // 切換到獨立啟動面板
            ShowPanel(LaunchingPanel);
            LaunchVersionText.Text = version;
            LaunchTitleText.Text = "正在启动游戏";
            CurrentStepText.Text = "检查文件...";
            LoginTypeText.Text = AccountText.Text.Contains("正版") ? "正版" : "离线";
            LaunchProgressBar.Value = 0;
            LaunchProgressBar.IsIndeterminate = false;

            StatusText.Text = "正在启动...";
            try
            {
                var option = new MLaunchOption 
                { 
                    MaximumRamMb = _config.MaxMemory, 
                    Session = _currentSession, 
                    FullScreen = _config.FullScreen, 
                    JavaPath = string.IsNullOrWhiteSpace(_config.JavaPath) ? null : _config.JavaPath
                };

                if (!string.IsNullOrWhiteSpace(_config.JvmArgs)) 
                    option.ExtraJvmArguments = _config.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => new MArgument(a)).ToArray();

                // 進度條回調
                _launcher.FileProgressChanged += (s, progress) => Dispatcher.Invoke(() => {
                    if (progress.TotalTasks > 0)
                    {
                        LaunchProgressBar.Value = progress.ProgressedTasks * 100.0 / progress.TotalTasks;
                        CurrentStepText.Text = $"下载: {progress.Name}";
                    }
                });

                CurrentStepText.Text = "校验资源文件...";
                await _launcher.InstallAsync(version);
                
                CurrentStepText.Text = "生成启动命令...";
                var process = await _launcher.BuildProcessAsync(version, option);
                
                CurrentStepText.Text = "等待游戏窗口出现...";
                process.Start();

                LaunchTitleText.Text = "已启动游戏";
                CurrentStepText.Text = "游戏运行中";
                LaunchProgressBar.Value = 100;
                LaunchProgressBar.IsIndeterminate = true;
                StatusText.Text = $"启动完成！({version})";
                _isLaunching = false; // 允许返回主页

                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (process.ExitCode != 0) 
                        { 
                            LaunchTitleText.Text = "游戏崩溃了"; 
                            CurrentStepText.Text = "请查看日志"; 
                            MessageBox.Show(GetLatestCrashLog(version), "崩溃分析"); 
                        }
                        else 
                        {
                            LaunchTitleText.Text = "游戏已退出";
                            CurrentStepText.Text = "正常退出";
                        }
                        LaunchProgressBar.IsIndeterminate = false;
                        LaunchProgressBar.Value = 0;
                        ShowPanel(HomePanel); // 退出后自动返回主页
                    });
                };
            }
            catch (Exception ex) 
            { 
                LaunchTitleText.Text = "启动失败";
                CurrentStepText.Text = "发生错误";
                LaunchProgressBar.IsIndeterminate = false;
                MessageBox.Show($"启动失败:\n{ex.Message}"); 
                ShowPanel(HomePanel);
            }
        }

        private void CancelLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (_isLaunching)
            {
                var result = MessageBox.Show("游戏正在启动中，确定要返回主页吗？（启动过程可能仍在后台继续）", "提示", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    ShowPanel(HomePanel);
                }
            }
            else
            {
                ShowPanel(HomePanel);
            }
        }

        // ================= 以下為原有的輔助邏輯 =================
        private void ShowServerPanel(object sender, RoutedEventArgs e) => ShowPanel(ServerPanel);
        private async void QueryServer_Click(object sender, RoutedEventArgs e)
        {
            string ip = ServerIpBox.Text.Trim();
            if (string.IsNullOrEmpty(ip)) return;
            ServerResultCard.Visibility = Visibility.Visible;
            ServerStatusText.Text = "查詢中...";
            try
            {
                string url = $"https://api.mcsrvstat.us/3/{ip}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                if (json["online"]?.ToObject<bool>() ?? false)
                {
                    ServerStatusText.Text = "🟢 在線 (Online)"; ServerStatusText.Foreground = Brushes.LightGreen;
                    ServerPlayersText.Text = $"👥 玩家: {json["players"]?["online"]} / {json["players"]?["max"]}";
                    ServerVersionText.Text = $"⚙️ 版本: {json["version"]}";
                }
                else { ServerStatusText.Text = "🔴 離線 (Offline)"; ServerStatusText.Foreground = Brushes.Red; ServerPlayersText.Text = ""; ServerVersionText.Text = ""; }
            }
            catch { ServerStatusText.Text = "查詢失敗"; ServerStatusText.Foreground = Brushes.Red; }
        }

        private void ShowLogPanel(object sender, RoutedEventArgs e) { ShowPanel(LogPanel); RefreshLog_Click(null, null); }
        private void RefreshLog_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                string version = VersionCombo.SelectedItem?.ToString() ?? "";
                string baseDir = _config.VersionIsolation && !string.IsNullOrEmpty(version) ? Path.Combine(_launcher.MinecraftPath.BasePath, "versions", version) : _launcher.MinecraftPath.BasePath;
                string logPath = Path.Combine(baseDir, "logs", "latest.log");
                LogTextBox.Text = File.Exists(logPath) ? File.ReadAllText(logPath) : "未找到日誌檔案 (latest.log)。";
            }
            catch (Exception ex) { LogTextBox.Text = $"讀取日誌失敗: {ex.Message}"; }
        }

        private void DownloadJava_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("這將打開瀏覽器前往 Adoptium 官方網站下載 Java 21。\n是否前往？", "下載 Java 21", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("https://adoptium.net/temurin/releases/?version=21") { UseShellExecute = true });
        }

        private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MemoryTextDisplay != null) MemoryTextDisplay.Text = e.NewValue.ToString("F0");
        }

        private async Task LoadFullVersionManifestAsync()
        {
            VanillaListPanel.Children.Clear();
            StatusText.Text = "正在獲取 Mojang 版本清單...";
            try
            {
                var manifest = await _launcher.GetAllVersionsAsync();
                var releases = manifest.Where(v => v.Type == "release").Cast<dynamic>().ToList();
                var snapshots = manifest.Where(v => v.Type == "snapshot").Cast<dynamic>().ToList();
                var old_versions = manifest.Where(v => v.Type == "old_alpha" || v.Type == "old_beta").Cast<dynamic>().ToList();
                VanillaListPanel.Children.Add(CreateExpander($"正式版 ({releases.Count})", releases, true));
                VanillaListPanel.Children.Add(CreateExpander($"快照版 ({snapshots.Count})", snapshots, false));
                VanillaListPanel.Children.Add(CreateExpander($"遠古版 ({old_versions.Count})", old_versions, false));
                StatusText.Text = "版本清單獲取完成。";
            }
            catch (Exception ex) { StatusText.Text = "獲取清單失敗: " + ex.Message; }
        }

        private void ReloadMinecraftPath()
        {
            try
            {
                var path = new MinecraftPath(_config.MinecraftDir);
                _launcher = new MinecraftLauncher(path);
                _currentSession = MSession.CreateOfflineSession(_config.Username);
                AccountText.Text = $"{_config.Username} (離線)";
                CurrentMinecraftDirText.Text = _config.MinecraftDir;
                MinecraftDirBox.Text = _config.MinecraftDir;
                _ = LoadInstalledVersionsAsync();
                _ = LoadBaseVersionsForLoaderAsync();
                if (VanillaListPanel != null) VanillaListPanel.Children.Clear();
            }
            catch (Exception ex) { MessageBox.Show("路徑載入失敗: " + ex.Message); }
        }

        private void ChangeMinecraftDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) { _config.MinecraftDir = dialog.FolderName; SaveConfig(); ReloadMinecraftPath(); StatusText.Text = "已切換遊戲目錄！"; }
        }

        private void ApplyWallpaper()
        {
            try { if (!string.IsNullOrEmpty(_config.WallpaperPath) && File.Exists(_config.WallpaperPath)) BgImageBrush.ImageSource = new BitmapImage(new Uri(_config.WallpaperPath)); } catch { }
        }

        private void ChangeWallpaper_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "圖片|*.jpg;*.png;*.jpeg" };
            if (ofd.ShowDialog() == true) { _config.WallpaperPath = ofd.FileName; ApplyWallpaper(); SaveConfig(); StatusText.Text = "壁紙已更新！"; }
        }

        private void UpdateMemoryInfo()
        {
            double totalGB = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 1);
            MemoryInfoText.Text = $"系統內存: {totalGB} GB";
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                foreach (var file in (string[])e.Data.GetData(DataFormats.FileDrop))
                {
                    if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
                        Directory.CreateDirectory(modsDir);
                        File.Copy(file, Path.Combine(modsDir, Path.GetFileName(file)), true);
                        StatusText.Text = $"已安裝 Mod: {Path.GetFileName(file)}"; LoadInstalledMods();
                    }
                    else if (file.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                        await InstallModpackFromFile(file);
                }
            }
        }

        private async Task InstallModpackFromFile(string filePath)
        {
            StatusText.Text = $"正在解析整合包...";
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                ZipFile.ExtractToDirectory(filePath, tempDir);
                string indexFile = Path.Combine(tempDir, "modrinth.index.json");
                if (File.Exists(indexFile))
                {
                    var json = JObject.Parse(File.ReadAllText(indexFile));
                    string name = json["name"]?.ToString() ?? "Unknown Modpack";
                    string versionId = json["versionId"]?.ToString() ?? "1.0";
                    string mcVersion = json["dependencies"]?["minecraft"]?.ToString() ?? "1.20.1";
                    string packName = $"{name}-{versionId}";
                    string targetDir = Path.Combine(_launcher.MinecraftPath.Versions, packName);
                    Directory.CreateDirectory(targetDir);
                    string overridesDir = Path.Combine(tempDir, "overrides");
                    if (Directory.Exists(overridesDir)) CopyDirectory(overridesDir, targetDir);
                    var files = json["files"] as JArray;
                    if (files != null)
                    {
                        foreach (var f in files)
                        {
                            string path = f["path"]?.ToString() ?? "";
                            string downloadUrl = f["downloads"]?[0]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                string dest = Path.Combine(targetDir, path);
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                File.WriteAllBytes(dest, await _httpClient.GetByteArrayAsync(downloadUrl));
                            }
                        }
                    }
                    File.WriteAllText(Path.Combine(targetDir, $"{packName}.json"), $@"{{ ""id"": ""{packName}"", ""inheritsFrom"": ""{mcVersion}"", ""type"": ""release"" }}");
                    await _launcher.InstallAsync(packName);
                    await LoadInstalledVersionsAsync();
                }
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex) { MessageBox.Show($"整合包安裝失敗:\n{ex.Message}"); }
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relPath = file.Substring(sourceDir.Length + 1);
                string destFile = Path.Combine(destinationDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, true);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) { _discordClient?.Dispose(); Application.Current.Shutdown(); }
        private void ShowHome_Click(object sender, RoutedEventArgs e) => ShowPanel(HomePanel);
        private void ShowVersionManager_Click(object sender, RoutedEventArgs e) { ShowPanel(VersionManagerPanel); LoadVersionManagerList(); }
        private void ShowDownloadPage(object sender, RoutedEventArgs e) { ShowPanel(DownloadPanel); if (VanillaListPanel.Children.Count <= 1) _ = LoadFullVersionManifestAsync(); _ = LoadBaseVersionsForLoaderAsync(); }
        private void ShowModManagerPage(object sender, RoutedEventArgs e) { ShowPanel(ModManagerPanel); LoadInstalledMods(); }
        private void ShowModSearchPage(object sender, RoutedEventArgs e) => ShowPanel(ModsPanel);
        private void ShowAccountPage(object sender, RoutedEventArgs e) => ShowPanel(AccountPanel);
        private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowPanel(SettingsPanel);

        private void ShowVersionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (VersionCombo.SelectedItem == null) return;
            var win = new VersionSettingsWindow(VersionCombo.SelectedItem.ToString()!, _launcher.MinecraftPath.BasePath);
            win.ShowDialog();
            _ = LoadInstalledVersionsAsync();
        }

        private void ScanJava_Click(object sender, RoutedEventArgs e)
        {
            var javaVersions = new System.Collections.Generic.List<string>();
            foreach (var dir in new[] { @"C:\Program Files\Java", @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Microsoft", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
                if (Directory.Exists(dir)) javaVersions.AddRange(Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories));
            
            if (javaVersions.Count > 0) { MessageBox.Show($"找到 {javaVersions.Count} 個 Java", "掃描結果"); JavaPathBox.Text = javaVersions.Last(); }
            else MessageBox.Show("未找到 Java。");
        }

        private void LoadInstalledMods()
        {
            InstalledModsPanel.Children.Clear();
            string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
            if (!Directory.Exists(modsDir)) { InstalledModsPanel.Children.Add(new TextBlock { Text = "Mods 文件夾不存在。", Foreground = Brushes.Gray }); return; }
            
            foreach (var file in Directory.GetFiles(modsDir).Where(f => f.EndsWith(".jar") || f.EndsWith(".jar.disabled")))
            {
                bool isEnabled = file.EndsWith(".jar");
                string fileName = Path.GetFileName(file).Replace(".disabled", "");
                var card = new Border { Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var chk = new CheckBox { IsChecked = isEnabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                chk.Click += (s, e) => { try { File.Move(file, isEnabled ? file + ".disabled" : file.Replace(".disabled", "")); LoadInstalledMods(); } catch { } };
                stack.Children.Add(chk); stack.Children.Add(new TextBlock { Text = fileName, Foreground = Brushes.White });
                var delBtn = new Button { Content = "刪除", Appearance = ControlAppearance.Danger, Tag = file };
                delBtn.Click += (s, e) => { try { File.Delete(file); LoadInstalledMods(); } catch { } };
                Grid.SetColumn(stack, 0); Grid.SetColumn(delBtn, 1);
                grid.Children.Add(stack); grid.Children.Add(delBtn); card.Child = grid;
                InstalledModsPanel.Children.Add(card);
            }
        }

        private void ImportMod_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Mod 檔案|*.jar", Multiselect = true };
            if (ofd.ShowDialog() == true)
            {
                string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
                Directory.CreateDirectory(modsDir);
                foreach (var file in ofd.FileNames) File.Copy(file, Path.Combine(modsDir, Path.GetFileName(file)), true);
                LoadInstalledMods();
            }
        }

        private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
            Directory.CreateDirectory(modsDir);
            Process.Start("explorer.exe", modsDir);
        }

        private async void SearchMods_Click(object sender, RoutedEventArgs e)
        {
            string query = ModSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;
            ModListPanel.Children.Clear();
            try
            {
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=10&facets=[[\"project_type:mod\"]]";
                var json = JObject.Parse(await _httpClient.GetStringAsync(url));
                var hits = json["hits"];
                if (hits != null) foreach (var hit in hits) AddModCard(hit["title"]?.ToString() ?? "Unknown", hit["description"]?.ToString() ?? "", hit["icon_url"]?.ToString() ?? "", hit["project_id"]?.ToString() ?? "");
            }
            catch { }
        }

        private void AddModCard(string title, string desc, string iconUrl, string projectId)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var image = new Image { Width = 50, Height = 50, Margin = new Thickness(0, 0, 10, 0) };
            try { if (!string.IsNullOrEmpty(iconUrl)) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(iconUrl); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); image.Source = bitmap; } } catch { }
            Grid.SetColumn(image, 0);
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            textStack.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(textStack, 1);
            var btn = new Button { Content = "下載", Width = 80, Height = 30, Tag = projectId, Appearance = ControlAppearance.Primary };
            btn.Click += DownloadMod_Click;
            Grid.SetColumn(btn, 2);
            grid.Children.Add(image); grid.Children.Add(textStack); grid.Children.Add(btn);
            border.Child = grid; ModListPanel.Children.Add(border);
        }

        private async void DownloadMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string projectId)
            {
                btn.IsEnabled = false; btn.Content = "取得中...";
                try
                {
                    var versions = JArray.Parse(await _httpClient.GetStringAsync($"https://api.modrinth.com/v2/project/{projectId}/version"));
                    if (versions.Count > 0 && versions[0]?["files"]?[0] is JToken file)
                    {
                        string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
                        Directory.CreateDirectory(modsDir);
                        File.WriteAllBytes(Path.Combine(modsDir, file["filename"]?.ToString() ?? "mod.jar"), await _httpClient.GetByteArrayAsync(file["url"]?.ToString() ?? ""));
                        btn.Content = "已下載";
                    }
                }
                catch { btn.Content = "重試"; btn.IsEnabled = true; }
            }
        }

        private string GetLatestCrashLog(string version)
        {
            string baseDir = _config.VersionIsolation ? Path.Combine(_launcher.MinecraftPath.BasePath, "versions", version) : _launcher.MinecraftPath.BasePath;
            string crashDir = Path.Combine(baseDir, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var latestCrash = new DirectoryInfo(crashDir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latestCrash != null && latestCrash.LastWriteTime > DateTime.Now.AddMinutes(-2))
                    try { return "【崩潰報告】\n" + string.Join("\n", File.ReadLines(latestCrash.FullName).Take(30)) + "\n..."; } catch { }
            }
            return "未找到崩潰報告。";
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath)) try { var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)); if (loaded != null) _config = loaded; } catch { }
            MinecraftDirBox.Text = _config.MinecraftDir;
            MemorySlider.Value = _config.MaxMemory;
            MemoryTextDisplay.Text = _config.MaxMemory.ToString();
            JavaPathBox.Text = _config.JavaPath;
            DiscordRpcCheck.IsChecked = _config.EnableDiscordRpc;
            FullScreenCheck.IsChecked = _config.FullScreen;
            VersionIsolationCheck.IsChecked = _config.VersionIsolation;
            _currentSession = MSession.CreateOfflineSession(_config.Username);
            AccountText.Text = $"{_config.Username} (離線)";
        }

        private void SaveConfig() => File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _config.MaxMemory = (int)MemorySlider.Value;
            _config.JavaPath = JavaPathBox.Text;
            _config.EnableDiscordRpc = DiscordRpcCheck.IsChecked ?? true;
            _config.FullScreen = FullScreenCheck.IsChecked ?? false;
            _config.VersionIsolation = VersionIsolationCheck.IsChecked ?? true;
            SaveConfig();
            StatusText.Text = "設置已保存！";
        }

        private void LoadSavedAccounts()
        {
            SavedAccountsPanel.Children.Clear();
            foreach (var acc in _config.OfflineAccounts)
            {
                var btn = new Button { Content = acc, Margin = new Thickness(0, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
                btn.Click += (s, e) => { _currentSession = MSession.CreateOfflineSession(acc); AccountText.Text = $"{acc} (離線)"; ShowPanel(HomePanel); };
                SavedAccountsPanel.Children.Add(btn);
            }
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            string name = NewOfflineNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!_config.OfflineAccounts.Contains(name)) _config.OfflineAccounts.Add(name);
            SaveConfig(); LoadSavedAccounts(); NewOfflineNameBox.Text = "";
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            try { var session = await JELoginHandlerBuilder.BuildDefault().Authenticate(); if (session != null) { _currentSession = session; AccountText.Text = $"{session.Username} (正版)"; } } catch { }
        }

        private void InitDiscordRpc()
        {
            if (!_config.EnableDiscordRpc) return;
            try { _discordClient?.Dispose(); _discordClient = new DiscordRpcClient("123456789012345678"); _discordClient.Initialize(); } catch { }
        }

        private Task LoadInstalledVersionsAsync()
        {
            try
            {
                VersionCombo.Items.Clear();
                string versionsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "versions");
                if (Directory.Exists(versionsDir))
                    foreach (var dir in Directory.GetDirectories(versionsDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (File.Exists(Path.Combine(dir, $"{name}.json"))) VersionCombo.Items.Add(name);
                    }
                if (VersionCombo.Items.Count > 0) VersionCombo.SelectedIndex = 0;
                else VersionCombo.Items.Add("未安装任何版本");
            }
            catch { }
            return Task.CompletedTask;
        }

        private async Task LoadBaseVersionsForLoaderAsync()
        {
            try
            {
                LoaderBaseVersionCombo.Items.Clear();
                var manifest = await _launcher.GetAllVersionsAsync();
                foreach (var v in manifest.Where(v => v.Type == "release")) LoaderBaseVersionCombo.Items.Add(v.Name);
                if (LoaderBaseVersionCombo.Items.Count > 0) LoaderBaseVersionCombo.SelectedIndex = 0;
            }
            catch { }
        }

        private Expander CreateExpander(string header, System.Collections.Generic.IEnumerable<dynamic> versions, bool isExpanded)
        {
            var expander = new Expander { Header = header, IsExpanded = isExpanded, Margin = new Thickness(0,0,0,15), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var v in versions) wrap.Children.Add(CreateDownloadBtn(v.Name.ToString()));
            expander.Content = wrap;
            return expander;
        }

        private Button CreateDownloadBtn(string version)
        {
            var btn = new Button { Content = version, Width = 100, Height = 35, Margin = new Thickness(0, 0, 10, 10), Tag = version };
            btn.Click += Download_Click;
            return btn;
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) { MessageBox.Show("請等待當前任務完成！"); return; }
            if (sender is Button btn && btn.Tag is string version)
            {
                _isInstalling = true; btn.IsEnabled = false;
                try
                {
                    _launcher.FileProgressChanged += (s, progress) => Dispatcher.Invoke(() => {
                        if (progress.TotalTasks > 0) DownloadProgress.Value = progress.ProgressedTasks * 100.0 / progress.TotalTasks;
                    });
                    await _launcher.InstallAsync(version);
                    await LoadInstalledVersionsAsync();
                }
                catch (Exception ex) { MessageBox.Show($"下載失敗:\n{ex.Message}"); }
                finally { _isInstalling = false; btn.IsEnabled = true; }
            }
        }

        private async void InstallFabric_Click(object sender, RoutedEventArgs e)
        {
            if (LoaderBaseVersionCombo.SelectedItem == null) return;
            string baseVersion = LoaderBaseVersionCombo.SelectedItem.ToString()!;
            try
            {
                var loaderVersions = JArray.Parse(await _httpClient.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{baseVersion}"));
                if (loaderVersions.Count == 0) return;
                string loaderVersion = loaderVersions[0]["loader"]?["version"]?.ToString() ?? "";
                string profileJson = await _httpClient.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{baseVersion}/{loaderVersion}/profile/json");
                string versionName = $"fabric-loader-{loaderVersion}-{baseVersion}";
                string versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionName);
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), profileJson);
                await _launcher.InstallAsync(versionName);
                await LoadInstalledVersionsAsync();
            }
            catch { }
        }

        private async void InstallForge_Click(object sender, RoutedEventArgs e)
        {
            if (LoaderBaseVersionCombo.SelectedItem == null) return;
            try { await new ForgeInstaller(_launcher).Install(LoaderBaseVersionCombo.SelectedItem.ToString()!); await LoadInstalledVersionsAsync(); } catch { }
        }

        private async void SearchModpacks_Click(object sender, RoutedEventArgs e)
        {
            string query = ModpackSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;
            ModpackListPanel.Children.Clear();
            try
            {
                var json = JObject.Parse(await _httpClient.GetStringAsync($"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=10&facets=[[\"project_type:modpack\"]]"));
                var hits = json["hits"];
                if (hits != null) foreach (var hit in hits) AddModpackCard(hit["title"]?.ToString() ?? "Unknown", hit["description"]?.ToString() ?? "", hit["icon_url"]?.ToString() ?? "", hit["project_id"]?.ToString() ?? "");
            }
            catch { }
        }

        private void AddModpackCard(string title, string desc, string iconUrl, string projectId)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var image = new Image { Width = 50, Height = 50, Margin = new Thickness(0, 0, 10, 0) };
            try { if (!string.IsNullOrEmpty(iconUrl)) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(iconUrl); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); image.Source = bitmap; } } catch { }
            Grid.SetColumn(image, 0);
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            textStack.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(textStack, 1);
            var btn = new Button { Content = "下載", Width = 80, Height = 30, Tag = projectId, Appearance = ControlAppearance.Primary };
            btn.Click += DownloadModpack_Click;
            Grid.SetColumn(btn, 2);
            grid.Children.Add(image); grid.Children.Add(textStack); grid.Children.Add(btn);
            border.Child = grid; ModpackListPanel.Children.Add(border);
        }

        private async void DownloadModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string projectId)
            {
                btn.IsEnabled = false; btn.Content = "取得中...";
                try
                {
                    var versions = JArray.Parse(await _httpClient.GetStringAsync($"https://api.modrinth.com/v2/project/{projectId}/version"));
                    if (versions.Count > 0 && versions[0]?["files"]?[0] is JToken file)
                    {
                        string packsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "modpacks");
                        Directory.CreateDirectory(packsDir);
                        string dest = Path.Combine(packsDir, file["filename"]?.ToString() ?? "modpack.mrpack");
                        File.WriteAllBytes(dest, await _httpClient.GetByteArrayAsync(file["url"]?.ToString() ?? ""));
                        await InstallModpackFromFile(dest);
                        btn.Content = "已安裝";
                    }
                }
                catch { btn.Content = "重試"; btn.IsEnabled = true; }
            }
        }

        private void RefreshVersionManager_Click(object sender, RoutedEventArgs e) => LoadVersionManagerList();

        private void LoadVersionManagerList()
        {
            VersionManagerList.Children.Clear();
            string versionsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "versions");
            if (!Directory.Exists(versionsDir)) return;
            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                string name = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, $"{name}.json"))) continue;
                var card = new Card { Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10), Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                info.Children.Add(new TextBlock { Text = $"路徑: {dir}", FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var openBtn = new Button { Content = "打開", Margin = new Thickness(5, 0, 0, 0), Tag = dir };
                openBtn.Click += (s, e) => Process.Start("explorer.exe", dir);
                var delBtn = new Button { Content = "刪除", Appearance = ControlAppearance.Danger, Margin = new Thickness(5, 0, 0, 0), Tag = dir };
                delBtn.Click += (s, e) => { if (MessageBox.Show($"確定刪除 {name} 嗎？", "警告", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { try { Directory.Delete(dir, true); LoadVersionManagerList(); } catch { } } };
                btnPanel.Children.Add(openBtn); btnPanel.Children.Add(delBtn);
                Grid.SetColumn(info, 0); Grid.SetColumn(btnPanel, 1);
                grid.Children.Add(info); grid.Children.Add(btnPanel); card.Content = grid;
                VersionManagerList.Children.Add(card);
            }
        }
    }
}