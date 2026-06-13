using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    public partial class WeatherWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private DispatcherTimer? _refreshTimer;
        private readonly AppSettings _settings;

        // Win32 API for window layering
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_BOTTOM   = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST  = new IntPtr(-1);
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private bool _isPositioning = false;   // 程序化定位中，抑制 LocationChanged 回写

        // ── 构造函数 ───────────────────────────────────────────
        public WeatherWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            // 拖动时实时更新坐标到 settings
            LocationChanged += Window_LocationChanged;

            // 窗口完成布局后再定位（SizeToContent 计算完成后）
            Loaded += OnFirstLoaded;
        }

        private void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            PositionWindow();
        }

        /// <summary>根据当前设置初始化窗口样式</summary>
        public void ApplyMode()
        {
            int mode = _settings.WeatherWindowMode;

            if (mode == 1) // 窗口模式
            {
                MainBorder.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#E815152A"));
                MainBorder.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#30FFFFFF"));
                MainBorder.BorderThickness = new Thickness(1);
                MainBorder.CornerRadius = new CornerRadius(12);
                MainBorder.Padding = new Thickness(14, 12, 14, 12);
                MainBorder.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 3,
                    Opacity = 0.5
                };
            }
            else // 文字模式
            {
                MainBorder.Background = Brushes.Transparent;
                MainBorder.BorderThickness = new Thickness(0);
                MainBorder.CornerRadius = new CornerRadius(0);
                MainBorder.Padding = new Thickness(4, 2, 4, 2);
                MainBorder.Effect = null;
            }

            ApplyFontSize();
            ApplyColors();
            ApplyWindowLayer();
            // SizeToContent 自动调整窗口大小，无需手动设 Width/Height
        }

        /// <summary>应用字体大小到所有文本</summary>
        public void ApplyFontSize()
        {
            double size = _settings.WeatherFontSize > 0 ? _settings.WeatherFontSize : 14;
            double iconSize = size * 1.5;   // 图标是文字的 1.5 倍
            double subSize  = size * 0.75; // 辅助信息小一些
            double timeSize = size * 0.7;

            WeatherIconTb.FontSize   = iconSize;
            CityTb.FontSize          = size + 2;
            WeatherTb.FontSize       = size;
            TempTb.FontSize          = size + 6;
            WindTb.FontSize          = subSize;
            HumidityTb.FontSize      = subSize;
            ReportTimeTb.FontSize    = timeSize;
        }

        /// <summary>应用文字颜色到所有文本元素</summary>
        public void ApplyColors()
        {
            CityTb.Foreground          = ConvertColor(_settings.WeatherCityColor);
            WeatherTb.Foreground      = ConvertColor(_settings.WeatherInfoColor);
            TempTb.Foreground         = ConvertColor(_settings.WeatherTempColor);
            WindTb.Foreground         = ConvertColor(_settings.WeatherInfoColor);
            HumidityTb.Foreground     = ConvertColor(_settings.WeatherInfoColor);
            ReportTimeTb.Foreground   = ConvertColor(_settings.WeatherTimeColor);
            WeatherIconTb.Foreground  = ConvertColor(_settings.WeatherIconColor);
        }

        private static Brush ConvertColor(string hex)
        {
            try { return new BrushConverter().ConvertFromString(hex) as Brush ?? Brushes.White; }
            catch { return Brushes.White; }
        }

        /// <summary>应用窗口层级</summary>
        public void ApplyWindowLayer()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (_settings.WeatherAlwaysOnTop)
            {
                Topmost = true;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            else
            {
                Topmost = false;
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
        }

        /// <summary>定位窗口（支持预设位置 + 自定义坐标）</summary>
        public void PositionWindow()
        {
            // SizeToContent 模式下，首次调用时宽高可能为 NaN，跳过
            if (double.IsNaN(Width) || double.IsNaN(Height)) return;

            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;

            _isPositioning = true;

            int preset = _settings.WeatherWindowPreset;
            if (preset == 0) // 自定义坐标
            {
                double cx = _settings.WeatherCustomX;
                double cy = _settings.WeatherCustomY;

                if (cx >= 0 && cy >= 0)
                {
                    Left = cx;
                    Top  = cy;
                }
                else
                {
                    // 兜底：右上角
                    Left = sw - ActualWidth - 20;
                    Top  = 60;
                }
            }
            else
            {
                // 预设位置
                switch (preset)
                {
                    case 1: // 右上角
                        Left = sw - ActualWidth - 20;
                        Top  = 60;
                        break;
                    case 2: // 右下角
                        Left = sw - ActualWidth - 20;
                        Top  = sh - ActualHeight - 40;
                        break;
                    case 3: // 左上角
                        Left = 20;
                        Top  = 60;
                        break;
                    case 4: // 左下角
                        Left = 20;
                        Top  = sh - ActualHeight - 40;
                        break;
                    case 5: // 顶部居中
                        Left = (sw - ActualWidth) / 2;
                        Top  = 20;
                        break;
                    default: // fallback 右上角
                        Left = sw - ActualWidth - 20;
                        Top  = 60;
                        break;
                }
            }

            // 确保不超出屏幕
            if (Left < 0) Left = 0;
            if (Top < 0) Top = 0;
            if (Left + ActualWidth > sw) Left = sw - ActualWidth;
            if (Top + ActualHeight > sh) Top = sh - ActualHeight;

            _isPositioning = false;
        }

        // ── 天气 API ────────────────────────────────────────

        /// <summary>加载天气数据</summary>
        public async Task LoadWeatherAsync()
        {
            try
            {
                string city = string.IsNullOrWhiteSpace(_settings.WeatherCity)
                    ? "北京" : _settings.WeatherCity.Trim();
                string adcode = (_settings.WeatherAdcode ?? "").Trim();
                string url = $"https://uapis.cn/api/v1/misc/weather?city={Uri.EscapeDataString(city)}" +
                             $"&adcode={Uri.EscapeDataString(adcode)}" +
                             "&extended=false&forecast=false&hourly=false&minutely=false&indices=false&lang=zh";

                var json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string province  = root.TryGetProperty("province", out var p)  ? p.GetString() ?? "" : "";
                string rCity     = root.TryGetProperty("city", out var c)     ? c.GetString() ?? "" : "";
                string district  = root.TryGetProperty("district", out var d) ? d.GetString() ?? "" : "";
                string weather   = root.TryGetProperty("weather", out var w)  ? w.GetString() ?? "" : "";
                string wIcon     = root.TryGetProperty("weather_icon", out var wi) ? wi.GetString() ?? "" : "";
                int temperature  = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number
                    ? (int)t.GetDouble() : 0;
                string windDir   = root.TryGetProperty("wind_direction", out var wd) ? wd.GetString() ?? "" : "";
                string windPower = root.TryGetProperty("wind_power", out var wp) ? wp.GetString() ?? "" : "";
                int humidity     = root.TryGetProperty("humidity", out var h) && h.ValueKind == JsonValueKind.Number
                    ? (int)h.GetDouble() : 0;
                string reportTime = root.TryGetProperty("report_time", out var rt) ? rt.GetString() ?? "" : "";

                // 决定显示地点：优先 district，其次 city，再 province
                string location = !string.IsNullOrWhiteSpace(district) ? district
                    : !string.IsNullOrWhiteSpace(rCity) ? rCity
                    : !string.IsNullOrWhiteSpace(province) ? province : city;

                await Dispatcher.InvokeAsync(() =>
                {
                    WeatherIconTb.Text = GetWeatherEmoji(wIcon);
                    CityTb.Text       = location;
                    WeatherTb.Text    = weather;
                    TempTb.Text       = $"{temperature}°C";
                    WindTb.Text       = !string.IsNullOrWhiteSpace(windDir)
                        ? $"{windDir} {windPower}".Trim() : "--";
                    HumidityTb.Text   = humidity > 0 ? $"湿度 {humidity}%" : "--";
                    ReportTimeTb.Text = reportTime;
                });
            }
            catch
            {
                // 网络异常静默处理，保留上次数据
            }
        }

        /// <summary>根据天气图标代码返回 emoji</summary>
        private static string GetWeatherEmoji(string? iconCode)
        {
            return iconCode switch
            {
                "100" => "☀",   // 晴
                "101" => "🌤",  // 多云
                "102" => "⛅",  // 少云
                "103" => "⛅",  // 晴间多云
                "104" => "☁",   // 阴
                "200" => "🌦",  // 阵雨
                "300" => "🌧",  // 雷阵雨
                "301" => "⛈",  // 雷阵雨伴冰雹
                "400" => "❄",   // 小雪
                "500" => "🌫",  // 雾
                _     => "🌤"
            };
        }

        // ── 自动刷新定时器 ─────────────────────────────────

        public void StartRefreshTimer()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;

            int intervalMin = _settings.WeatherRefreshInterval;
            if (intervalMin <= 0) return;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMin)
            };
            _refreshTimer.Tick += async (_, _) => await LoadWeatherAsync();
            _refreshTimer.Start();
        }

        // ── 窗口事件 ────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                // 拖动时自动切换到自定义坐标模式
                if (_settings.WeatherWindowPreset != 0)
                {
                    _settings.WeatherWindowPreset = 0;
                }
                DragMove();
            }
            else if (e.ClickCount == 2)
            {
                // 双击刷新
                _ = LoadWeatherAsync();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
        }

        /// <summary>拖动窗口时实时同步坐标到 settings，确保"确定"后不掉回原位</summary>
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_isPositioning) return;
            // 只在自定义模式（preset=0）时回写坐标；其他模式不污染自定义值
            if (_settings.WeatherWindowPreset != 0) return;
            _settings.WeatherCustomX = Left;
            _settings.WeatherCustomY = Top;
        }
    }
}
