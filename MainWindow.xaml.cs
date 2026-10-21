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

// 解決所有命名衝突
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
        
        // 核心修改 1：將設定檔路徑改到 AppData，避免安裝在 Program Files 時無法寫入
        private string _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CatMin", "launcher_config.json");
        
        private static readonly HttpClient _httpClient = new HttpClient();

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
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CatMinLauncher/2.0.0");

            // 確保 AppData\Roaming\CatMin 資料夾存在
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

            LoadConfig();
            ReloadMinecraftPath();
            
            ApplyWallpaper();
            InitDiscordRpc();
            UpdateMemoryInfo();
            LoadSavedAccounts();
        }

        // ================= 切換遊戲目錄核心 =================
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
            catch (Exception ex)
            {
                MessageBox.Show("路徑載入失敗: " + ex.Message);
            }
        }

        private void ChangeMinecraftDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Title = "選擇 Minecraft 遊戲目錄 (包含 versions, mods 等)";
            
            if (dialog.ShowDialog() == true)
            {
                _config.MinecraftDir = dialog.FolderName;
                SaveConfig();
                ReloadMinecraftPath();
                StatusText.Text = "已切換遊戲目錄！";
            }
        }

        // ================= 壁紙與視窗控制 =================
        private void ApplyWallpaper()
        {
            try
            {
                if (!string.IsNullOrEmpty(_config.WallpaperPath) && File.Exists(_config.WallpaperPath))
                {
                    BgImageBrush.ImageSource = new BitmapImage(new Uri(_config.WallpaperPath));
                }
            }
            catch { }
        }

        private void ChangeWallpaper_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "圖片|*.jpg;*.png;*.jpeg" };
            if (ofd.ShowDialog() == true)
            {
                _config.WallpaperPath = ofd.FileName;
                ApplyWallpaper();
                SaveConfig();
                StatusText.Text = "壁紙已更新！";
            }
        }

        private void UpdateMemoryInfo()
        {
            double totalGB = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 1);
            MemoryInfoText.Text = $"系統內存: {totalGB} GB";
        }

        private void AutoDetectMemory_Click(object sender, RoutedEventArgs e)
        {
            double totalGB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;
            int suggest = (int)(totalGB * 0.6) * 1024;
            if (suggest < 2048) suggest = 2048;
            if (suggest > 16384) suggest = 16384;
            MemoryBox.Text = suggest.ToString();
        }

        // ================= 拖拽安裝核心 =================
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
                        Directory.CreateDirectory(modsDir);
                        File.Copy(file, Path.Combine(modsDir, Path.GetFileName(file)), true);
                        StatusText.Text = $"已安裝 Mod: {Path.GetFileName(file)}";
                        LoadInstalledMods();
                    }
                    else if (file.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                    {
                        await InstallModpackFromFile(file);
                    }
                }
            }
        }

        private async Task InstallModpackFromFile(string filePath)
        {
            StatusText.Text = $"正在解析整合包: {Path.GetFileName(filePath)}...";
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
                        int current = 0;
                        foreach (var f in files)
                        {
                            string path = f["path"]?.ToString() ?? "";
                            string downloadUrl = f["downloads"]?[0]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                current++;
                                StatusText.Text = $"下載整合包文件 {current}/{files.Count}: {Path.GetFileName(path)}...";
                                string dest = Path.Combine(targetDir, path);
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                byte[] data = await _httpClient.GetByteArrayAsync(downloadUrl);
                                File.WriteAllBytes(dest, data);
                            }
                        }
                    }

                    string jsonContent = $@"{{ ""id"": ""{packName}"", ""inheritsFrom"": ""{mcVersion}"", ""type"": ""release"" }}";
                    File.WriteAllText(Path.Combine(targetDir, $"{packName}.json"), jsonContent);

                    StatusText.Text = $"整合包 {packName} 安裝成功！";
                    await _launcher.InstallAsync(packName);
                    await LoadInstalledVersionsAsync();
                }
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"整合包安裝失敗:\n{ex.Message}");
            }
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

        // ================= 各面板切換 =================
        private void ShowPanel(FrameworkElement panelToShow)
        {
            HomePanel.Visibility = Visibility.Collapsed;
            VersionManagerPanel.Visibility = Visibility.Collapsed;
            DownloadPanel.Visibility = Visibility.Collapsed;
            ModManagerPanel.Visibility = Visibility.Collapsed;
            ModsPanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;

            panelToShow.Visibility = Visibility.Visible;
            if (FindResource("PageTransition") is Storyboard anim) anim.Begin(panelToShow);
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
            string version = VersionCombo.SelectedItem.ToString()!;
            var win = new VersionSettingsWindow(version, _launcher.MinecraftPath.BasePath);
            win.ShowDialog();
            _ = LoadInstalledVersionsAsync();
        }

        // ================= Java 掃描 =================
        private void ScanJava_Click(object sender, RoutedEventArgs e)
        {
            var javaVersions = new System.Collections.Generic.List<string>();
            string[] searchDirs = {
                @"C:\Program Files\Java",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Microsoft",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (var dir in searchDirs)
            {
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories))
                    {
                        javaVersions.Add(file);
                    }
                }
            }

            if (javaVersions.Count > 0)
            {
                MessageBox.Show($"找到 {javaVersions.Count} 個 Java 環境，已自動填入最新的路徑。\n\n{javaVersions.Last()}", "掃描結果");
                JavaPathBox.Text = javaVersions.Last();
            }
            else
            {
                MessageBox.Show("未在常見目錄找到 Java，請手動選擇 javaw.exe。");
            }
        }

        // ================= Mod 管理 =================
        private void LoadInstalledMods()
        {
            InstalledModsPanel.Children.Clear();
            string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
            if (!Directory.Exists(modsDir)) { InstalledModsPanel.Children.Add(new TextBlock { Text = "Mods 文件夾不存在。", Foreground = Brushes.Gray }); return; }
            
            foreach (var file in Directory.GetFiles(modsDir))
            {
                if (!file.EndsWith(".jar") && !file.EndsWith(".jar.disabled")) continue;
                bool isEnabled = file.EndsWith(".jar");
                string fileName = Path.GetFileName(file).Replace(".disabled", "");

                var card = new Border { Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var chk = new CheckBox { IsChecked = isEnabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                
                chk.Click += (s, e) => {
                    try {
                        string newPath = isEnabled ? file + ".disabled" : file.Replace(".disabled", "");
                        File.Move(file, newPath);
                        LoadInstalledMods();
                    } catch { MessageBox.Show("切換失敗，文件可能被佔用。"); }
                };
                
                stack.Children.Add(chk);
                stack.Children.Add(new TextBlock { Text = fileName, Foreground = Brushes.White, FontSize = 14 });

                var delBtn = new Button { Content = "刪除", Appearance = ControlAppearance.Danger, Tag = file };
                delBtn.Click += (s, e) => { try { File.Delete(file); LoadInstalledMods(); } catch { } };

                Grid.SetColumn(stack, 0);
                Grid.SetColumn(delBtn, 1);
                grid.Children.Add(stack);
                grid.Children.Add(delBtn);
                card.Child = grid;
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
                StatusText.Text = "導入成功！";
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

            StatusText.Text = $"正在搜尋 Mod: {query}...";
            ModListPanel.Children.Clear();

            try
            {
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=10&facets=[[\"project_type:mod\"]]";
                var json = JObject.Parse(await _httpClient.GetStringAsync(url));
                var hits = json["hits"];

                if (hits != null)
                {
                    foreach (var hit in hits) AddModCard(hit["title"]?.ToString() ?? "Unknown", hit["description"]?.ToString() ?? "", hit["icon_url"]?.ToString() ?? "", hit["project_id"]?.ToString() ?? "");
                    StatusText.Text = $"搜尋完成，找到 {hits.Count()} 個結果。";
                }
            }
            catch { StatusText.Text = "搜尋失敗，請檢查網路。"; }
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
            textStack.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) });
            Grid.SetColumn(textStack, 1);

            var btn = new Button { Content = "下載", Width = 80, Height = 30, Tag = projectId, Appearance = ControlAppearance.Primary };
            btn.Click += DownloadMod_Click;
            Grid.SetColumn(btn, 2);

            grid.Children.Add(image); grid.Children.Add(textStack); grid.Children.Add(btn);
            border.Child = grid;
            ModListPanel.Children.Add(border);
        }

        private async void DownloadMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string projectId)
            {
                btn.IsEnabled = false; btn.Content = "取得中...";
                try
                {
                    string url = $"https://api.modrinth.com/v2/project/{projectId}/version";
                    var versions = JArray.Parse(await _httpClient.GetStringAsync(url));
                    if (versions.Count > 0 && versions[0]?["files"]?[0] is JToken file)
                    {
                        string downloadUrl = file["url"]?.ToString() ?? "";
                        string fileName = file["filename"]?.ToString() ?? "mod.jar";
                        string modsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "mods");
                        Directory.CreateDirectory(modsDir);
                        File.WriteAllBytes(Path.Combine(modsDir, fileName), await _httpClient.GetByteArrayAsync(downloadUrl));
                        StatusText.Text = $"{fileName} 下載完成！";
                        btn.Content = "已下載";
                    }
                }
                catch { btn.Content = "重試"; btn.IsEnabled = true; }
            }
        }

        // ================= 啟動與設定 =================
        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            // 防呆 1：檢查是否選擇了版本
            if (VersionCombo.SelectedItem == null || VersionCombo.SelectedItem.ToString() == "未安装任何版本") 
            {
                MessageBox.Show("請先選擇一個遊戲版本，或去下載中心下載！", "提示");
                return;
            }
            
            string version = VersionCombo.SelectedItem.ToString()!;

            // 防呆 2：檢查目錄是否存在
            if (!Directory.Exists(_config.MinecraftDir))
            {
                MessageBox.Show($"找不到遊戲目錄：\n{_config.MinecraftDir}\n\n請至設定更改目錄。", "錯誤");
                return;
            }

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

                await _launcher.InstallAsync(version);
                var process = await _launcher.BuildProcessAsync(version, option);
                process.Start();

                StatusText.Text = "等待遊戲窗口...";
                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 核心修改 3：將 version 傳入崩潰日誌分析，並支援版本隔離路徑
                        if (process.ExitCode != 0) 
                        { 
                            StatusText.Text = "遊戲崩潰了！"; 
                            MessageBox.Show(GetLatestCrashLog(version), "崩潰分析"); 
                        }
                        else 
                        {
                            StatusText.Text = "遊戲已正常退出。";
                        }
                    });
                };
                await Task.Delay(3000);
                if (!process.HasExited) StatusText.Text = $"啟動完成！({version})";
            }
            catch (Exception ex) { MessageBox.Show($"啟動失敗:\n{ex.Message}"); }
        }

        // 核心修改 2：超級崩潰日誌讀取（支援版本隔離與 latest.log）
        private string GetLatestCrashLog(string version)
        {
            // 判斷是否啟用版本隔離，決定崩潰日誌的真實路徑
            string baseDir = _config.VersionIsolation 
                ? Path.Combine(_launcher.MinecraftPath.BasePath, "versions", version) 
                : _launcher.MinecraftPath.BasePath;

            // 優先找 crash-reports
            string crashDir = Path.Combine(baseDir, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var latestCrash = new DirectoryInfo(crashDir).GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                
                if (latestCrash != null && latestCrash.LastWriteTime > DateTime.Now.AddMinutes(-2))
                {
                    try { return "【崩潰報告】\n" + string.Join("\n", File.ReadLines(latestCrash.FullName).Take(30)) + "\n..."; } catch { }
                }
            }

            // 如果沒有崩潰報告，去抓 logs/latest.log
            string logPath = Path.Combine(baseDir, "logs", "latest.log");
            if (File.Exists(logPath))
            {
                var latestLog = new FileInfo(logPath);
                if (latestLog.LastWriteTime > DateTime.Now.AddMinutes(-2))
                {
                    try 
                    {
                        var lines = File.ReadLines(logPath).ToList();
                        var lastLines = lines.Skip(Math.Max(0, lines.Count - 30));
                        return "【遊戲日誌 (最後30行)】\n" + string.Join("\n", lastLines); 
                    } 
                    catch { }
                }
            }

            return "未找到崩潰報告或日誌。\n\n可能原因：\n1. Java 版本不匹配\n2. 內存分配過大導致無法啟動\n3. 自訂 JVM 參數錯誤\n4. 顯卡驅動崩潰\n5. 權限不足（已為您將設定檔移至 AppData，請嘗試重新啟動）";
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try { var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)); if (loaded != null) _config = loaded; } catch { }
            }
            MinecraftDirBox.Text = _config.MinecraftDir;
            MemoryBox.Text = _config.MaxMemory.ToString();
            JavaPathBox.Text = _config.JavaPath; JvmArgsBox.Text = _config.JvmArgs;
            DiscordRpcCheck.IsChecked = _config.EnableDiscordRpc; 
            FullScreenCheck.IsChecked = _config.FullScreen;
            VersionIsolationCheck.IsChecked = _config.VersionIsolation;
            
            _currentSession = MSession.CreateOfflineSession(_config.Username);
            AccountText.Text = $"{_config.Username} (離線)";
        }

        private void SaveConfig()
        {
            try
            {
                File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存設定失敗，請檢查 AppData 權限: {ex.Message}");
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _config.MaxMemory = int.TryParse(MemoryBox.Text, out int mem) ? mem : 4096;
            _config.JavaPath = JavaPathBox.Text; _config.JvmArgs = JvmArgsBox.Text;
            _config.EnableDiscordRpc = DiscordRpcCheck.IsChecked ?? true; 
            _config.FullScreen = FullScreenCheck.IsChecked ?? false;
            _config.VersionIsolation = VersionIsolationCheck.IsChecked ?? true;
            
            SaveConfig();
            StatusText.Text = "設置已保存！";
        }

        // ================= 帳號管理 =================
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
            SaveConfig();
            LoadSavedAccounts();
            NewOfflineNameBox.Text = "";
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var session = await JELoginHandlerBuilder.BuildDefault().Authenticate();
                if (session != null) { _currentSession = session; AccountText.Text = $"{session.Username} (正版)"; }
            }
            catch { StatusText.Text = "登錄失敗。"; }
        }

        private void InitDiscordRpc()
        {
            if (!_config.EnableDiscordRpc) return;
            try { _discordClient?.Dispose(); _discordClient = new DiscordRpcClient("123456789012345678"); _discordClient.Initialize(); } catch { }
        }

        // ================= 版本加載與下載 =================
        private Task LoadInstalledVersionsAsync()
        {
            try
            {
                VersionCombo.Items.Clear();
                string versionsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "versions");
                if (Directory.Exists(versionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(versionsDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (File.Exists(Path.Combine(dir, $"{name}.json"))) VersionCombo.Items.Add(name);
                    }
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
            if (sender is Button btn && btn.Tag is string version)
            {
                StatusText.Text = $"正在下載 {version}...";
                DownloadProgress.Value = 0;
                try
                {
                    _launcher.FileProgressChanged += (s, progress) => Dispatcher.Invoke(() => {
                        if (progress.TotalTasks > 0) DownloadProgress.Value = progress.ProgressedTasks * 100.0 / progress.TotalTasks;
                        StatusText.Text = $"正在下載: {progress.Name}";
                    });

                    await _launcher.InstallAsync(version);
                    StatusText.Text = $"{version} 下載完成！";
                    DownloadProgress.Value = 100;
                    await LoadInstalledVersionsAsync();
                }
                catch (Exception ex) { MessageBox.Show($"下載失敗:\n{ex.Message}"); }
            }
        }

        // ================= 加載器下載 =================
        private async void InstallFabric_Click(object sender, RoutedEventArgs e)
        {
            if (LoaderBaseVersionCombo.SelectedItem == null) return;
            string baseVersion = LoaderBaseVersionCombo.SelectedItem.ToString()!;
            StatusText.Text = $"正在從 Fabric API 獲取 {baseVersion} 的加載器...";
            try
            {
                string loaderUrl = $"https://meta.fabricmc.net/v2/versions/loader/{baseVersion}";
                var loaderVersions = JArray.Parse(await _httpClient.GetStringAsync(loaderUrl));
                if (loaderVersions.Count == 0) throw new Exception("該版本沒有可用的 Fabric 加載器。");
                
                string loaderVersion = loaderVersions[0]["loader"]?["version"]?.ToString() ?? "";
                string profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{baseVersion}/{loaderVersion}/profile/json";
                string profileJson = await _httpClient.GetStringAsync(profileUrl);

                string versionName = $"fabric-loader-{loaderVersion}-{baseVersion}";
                string versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionName);
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(Path.Combine(versionDir, $"{versionName}.json"), profileJson);

                StatusText.Text = $"Fabric 安裝成功: {versionName}";
                await _launcher.InstallAsync(versionName);
                await LoadInstalledVersionsAsync();
            }
            catch (Exception ex) { MessageBox.Show($"Fabric 安裝失敗:\n{ex.Message}"); }
        }

        private async void InstallForge_Click(object sender, RoutedEventArgs e)
        {
            if (LoaderBaseVersionCombo.SelectedItem == null) return;
            string baseVersion = LoaderBaseVersionCombo.SelectedItem.ToString()!;
            StatusText.Text = $"正在安裝 Forge for {baseVersion}...";
            try
            {
                var forgeInstaller = new ForgeInstaller(_launcher);
                string forgeVersion = await forgeInstaller.Install(baseVersion);
                StatusText.Text = $"Forge 安裝成功: {forgeVersion}";
                await LoadInstalledVersionsAsync();
            }
            catch (Exception ex) { MessageBox.Show($"Forge 安裝失敗:\n{ex.Message}"); }
        }

        // ================= 整合包搜索 =================
        private async void SearchModpacks_Click(object sender, RoutedEventArgs e)
        {
            string query = ModpackSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;
            ModpackListPanel.Children.Clear();
            StatusText.Text = "正在搜尋整合包...";

            try
            {
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=10&facets=[[\"project_type:modpack\"]]";
                var json = JObject.Parse(await _httpClient.GetStringAsync(url));
                var hits = json["hits"];

                if (hits != null)
                {
                    foreach (var hit in hits) AddModpackCard(hit["title"]?.ToString() ?? "Unknown", hit["description"]?.ToString() ?? "", hit["icon_url"]?.ToString() ?? "", hit["project_id"]?.ToString() ?? "");
                    StatusText.Text = "搜尋完成。";
                }
            }
            catch { StatusText.Text = "搜尋失敗。"; }
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
            textStack.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) });
            Grid.SetColumn(textStack, 1);

            var btn = new Button { Content = "下載", Width = 80, Height = 30, Tag = projectId, Appearance = ControlAppearance.Primary };
            btn.Click += DownloadModpack_Click;
            Grid.SetColumn(btn, 2);

            grid.Children.Add(image); grid.Children.Add(textStack); grid.Children.Add(btn);
            border.Child = grid;
            ModpackListPanel.Children.Add(border);
        }

        private async void DownloadModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string projectId)
            {
                btn.IsEnabled = false; btn.Content = "取得中...";
                try
                {
                    string url = $"https://api.modrinth.com/v2/project/{projectId}/version";
                    var versions = JArray.Parse(await _httpClient.GetStringAsync(url));
                    if (versions.Count > 0 && versions[0]?["files"]?[0] is JToken file)
                    {
                        string downloadUrl = file["url"]?.ToString() ?? "";
                        string fileName = file["filename"]?.ToString() ?? "modpack.mrpack";
                        string packsDir = Path.Combine(_launcher.MinecraftPath.BasePath, "modpacks");
                        Directory.CreateDirectory(packsDir);
                        string dest = Path.Combine(packsDir, fileName);
                        File.WriteAllBytes(dest, await _httpClient.GetByteArrayAsync(downloadUrl));
                        StatusText.Text = $"{fileName} 下載完成！正在解析安裝...";
                        btn.Content = "安裝中...";
                        await InstallModpackFromFile(dest);
                        btn.Content = "已安裝";
                    }
                }
                catch { btn.Content = "重試"; btn.IsEnabled = true; }
            }
        }

        // ================= 其他小功能 =================
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
                delBtn.Click += (s, e) => {
                    if (MessageBox.Show($"確定刪除 {name} 嗎？", "警告", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        try { Directory.Delete(dir, true); LoadVersionManagerList(); } catch { }
                    }
                };

                btnPanel.Children.Add(openBtn);
                btnPanel.Children.Add(delBtn);
                Grid.SetColumn(info, 0);
                Grid.SetColumn(btnPanel, 1);
                grid.Children.Add(info);
                grid.Children.Add(btnPanel);
                card.Content = grid;
                VersionManagerList.Children.Add(card);
            }
        }
    }
}