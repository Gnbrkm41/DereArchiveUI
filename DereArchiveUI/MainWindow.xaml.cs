using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using System.Windows;

using WebView2.DevTools.Dom;

using File = System.IO.File;
using Window = System.Windows.Window;

namespace DereArchiveUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitializeWebView();

            idolListGrid.ItemsSource = _idolInfos;
            commuListGrid.ItemsSource = _commuInfos;
        }
        private string _workingDirectory;
        private WebView2DevToolsContext _devToolsContext;
        private bool _isNavigated = false;
        private string? _token = "";
        private string? _pre = "";
        private ObservableCollection<IdolInfo> _idolInfos = new();
        record class IdolInfo(string Name, string Url) : INotifyPropertyChanged
        {
            private string _customName = "";
            public string CustomName 
            {
                get => _customName;
                set
                {
                    _customName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomName)));
                }
            }

            private bool _ignore = false;
            public bool Ignore
            {
                get => _ignore;
                set
                {
                    _ignore = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ignore)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private ObservableCollection<CommuInfo> _commuInfos = new();
        record class CommuInfo(string Url) : INotifyPropertyChanged
        {
            private string _customName = "";
            public string CustomName
            {
                get => _customName;
                set
                {
                    _customName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomName)));
                }
            }

            private bool _ignore = false;
            public bool Ignore
            {
                get => _ignore;
                set
                {
                    _ignore = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ignore)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
        public async void InitializeWebView()
        {
            string? processPath = Environment.ProcessPath;
            string processDir = processPath?[..processPath.LastIndexOf(Path.DirectorySeparatorChar)] ?? ".";
            _workingDirectory = processDir;
            CoreWebView2EnvironmentOptions envOptions = new CoreWebView2EnvironmentOptions();
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Join(processDir, "UserData"), envOptions);
            await webView.EnsureCoreWebView2Async(env);
#if !DEBUG
            await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
#endif
            webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Linux; Android 4.4.2; Nexus 4 Build/KOT49H) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/34.0.1847.114 Mobile Safari/537.36";
            webView.Source = new Uri("http://sp.pf.mbga.jp/12008305/");
            webView.NavigationCompleted += WebView_NavigationCompleted;
            _devToolsContext = await webView.CoreWebView2.CreateDevToolsContextAsync();
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                return;
            }

            // The app will try to connect to the top page of deremas, which results in login flow initially.
            // After all the login flow is complete, the page will (hopefully) be at the initial URL which is https://sp.pf.mbga.jp/12008305/.
            // Here, we try and read the cookies so that we can do stuffs:tm:
            if (webView.Source.AbsoluteUri == ("https://sp.pf.mbga.jp/12008305/"))
            {
                var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://sp.pf.mbga.jp/12008305");
                _token = cookies.FirstOrDefault(x => x.Name == "sp_mbga_sid_12008305")?.Value ?? "";
                _pre = cookies.FirstOrDefault(x => x.Name == "PRE")?.Value ?? "";
                if (_token != null && !_isNavigated)
                {
                    webView.Source = new Uri("https://sp.pf.mbga.jp/12008305/?guid=ON&url=http%3A%2F%2Fmobamas.net%2Fidolmaster%2Fidol_gallery%2Findex%2F0%2F0%2F1%2F0");
                    _isNavigated = true;
                }
            }
            else if (webView.Source.AbsoluteUri.StartsWith("https://sp.pf.mbga.jp/12008305"))
            {
                Uri originalUrl = webView.Source;
                NameValueCollection queries = HttpUtility.ParseQueryString(originalUrl.Query);
                if (queries["url"] == null)
                {
                    return;
                }

                var mobamasUrl = new Uri(queries["url"]!);
                if (mobamasUrl.LocalPath.StartsWith("/idolmaster/idol_gallery/idol_detail/"))
                {
                    var idolNameDiv = await _devToolsContext.QuerySelectorAsync<HtmlDivElement>(".detail_idol_real_name");
                    string name = await idolNameDiv.GetTextContentAsync();
                    if (!_idolInfos.Any(x => x.Name == name))
                    {
                        _idolInfos.Add(new(name, webView.Source.AbsoluteUri));
                    }
                }
            }
        }

        private void webView_CoreWebView2InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {

        }

        record Configuration([property: JsonPropertyName("workingPath")] string WorkingPath,
            [property: JsonPropertyName("token")] string Token,
            [property: JsonPropertyName("pre")] string Pre,
            [property: JsonPropertyName("idols")] Idol[] Idols,
            [property: JsonPropertyName("commus")] Commu[] Commus);

        private void ChangeProgramPathButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new() { AddExtension = true, DefaultExt = "exe", Filter = "응용 프로그램 (*.exe)|*.exe", FileName = "DereArchive.exe", CheckFileExists = true, ValidateNames = true, Title = "방주 프로그램 선택",  };
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            programPathTextBox.Text = dialog.FileName;
        }

        private void GoBackButton_Click(object sender, RoutedEventArgs e)
        {
            webView.GoBack();
        }

        private void AddCommuButton_Click(object sender, RoutedEventArgs e)
        {
            string customName = commuTitleTextBox.Text;
            char[] invalidChars = Path.GetInvalidPathChars();
            if (string.IsNullOrWhiteSpace(customName) || invalidChars.Any(ch => customName.Contains(ch)))
            {
                MessageBox.Show("저장명이 설정되지 않았거나 저장명에 폴더 이름으로 허용되지 않는 문자가 있습니다. 확인 후 재시도해주세요");
                return;
            }
            var duplicate = _commuInfos.FirstOrDefault(x => x.CustomName == customName);
            if (duplicate != null)
            {
                _commuInfos.Remove(duplicate);
            }
            _commuInfos.Add(new(webView.Source.AbsoluteUri) { CustomName = customName });
            commuTitleTextBox.Text = "";
        }

        private void GoForwardButton_Click(object sender, RoutedEventArgs e)
        {
            webView.GoForward();
        }

        record Idol([property: JsonPropertyName("idolName")] string IdolName,
            [property: JsonPropertyName("galleryUrl")] string GalleryUrl);
        record Commu([property: JsonPropertyName("commuName")] string CommuName,
            [property: JsonPropertyName("commuUrl")] string CommuUrl);
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var idols = _idolInfos.Where(x => !x.Ignore).Select(x => new Idol(x.CustomName, x.Url)).ToArray();
            var commus = _commuInfos.Where(x => !x.Ignore).Select(x => new Commu(x.CustomName, x.Url)).ToArray();
            var pre = _pre;
            var token = _token;
            var workingPath = workingDirectoryTextBox.Text;
            bool isValidPath = false;
            try
            {
                _ = new DirectoryInfo(workingPath);
                isValidPath = true;
            } catch {
                // Ignore
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("토큰이 올바르지 않습니다. 로그인 여부를 확인해주세요");
                return;
            }

            if (!isValidPath)
            {
                MessageBox.Show("경로가 올바르지 않습니다. 올바른 경로를 선택 후 재시도해주세요");
                return;
            }
            char[] invalidChars = Path.GetInvalidPathChars();
            if (idols.Any(x => string.IsNullOrEmpty(x.IdolName) || invalidChars.Any(ch => x.IdolName.Contains(ch))))
            {
                MessageBox.Show("갤러리 저장명이 설정되지 않았거나 저장명에 폴더 이름으로 허용되지 않는 문자가 있습니다. 확인 후 재시도해주세요");
                return;
            }

            if (commus.Any(x => string.IsNullOrEmpty(x.CommuName) || invalidChars.Any(ch => x.CommuName.Contains(ch))))
            {
                MessageBox.Show("커뮤 저장명이 설정되지 않았거나 저장명에 폴더 이름으로 허용되지 않는 문자가 있습니다. 확인 후 재시도해주세요");
                return;
            }

            string programPath = programPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(programPath))
            {
                MessageBox.Show("방주 프로그램 경로가 설정되지 않았습니다. 확인 후 재시도해주세요");
                return;
            }
            string programDirectory = string.Join(Path.DirectorySeparatorChar, programPath.Split(Path.DirectorySeparatorChar)[..^1]);
            string savePath = Path.Join(programDirectory, "configuration.json");
            string serialized = JsonSerializer.Serialize(new Configuration(workingPath, token, pre ?? "", idols, commus), new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(savePath, serialized);

            var result = MessageBox.Show("저장 완료! 프로그램을 실행할까요?", "완료", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(programPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = programDirectory
                });
            }
        }

        private void ChangeSaveDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog("저장할 폴더 선택") { IsFolderPicker = true, EnsureValidNames = true, EnsurePathExists = false, Multiselect = false };
            var result = dialog.ShowDialog(this);
            if (result != CommonFileDialogResult.Ok)
            {
                return;
            }
            workingDirectoryTextBox.Text = dialog.FileName;
        }
    }
}
