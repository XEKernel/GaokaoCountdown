using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    /// <summary>考试模式全屏倒计时窗口。按 ESC 或托盘菜单退出。</summary>
    public partial class ExamModeWindow : Window
    {
        private readonly ScheduleManager _manager;
        private readonly AppSettings _settings;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _weatherTimer;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        // ── 当前显示状态 ──────────────────────────────────────
        private string _currentSubjectName = string.Empty;
        private bool   _warnShown          = false;
        private bool   _flashRunning       = false;

        public ExamModeWindow(ScheduleManager manager, AppSettings settings)
        {
            _manager  = manager;
            _settings = settings;
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartTimer();
            Refresh();
            ApplyFontSizes();
            _ = LoadWeatherAsync();
            StartWeatherTimer();
        }

        /// <summary>应用考试模式字体大小设置</summary>
        public void ApplyFontSizes()
        {
            double baseFont = _settings.ExamModeFontSize;
            if (baseFont <= 0) baseFont = 32;
            CurrentTimeTb.FontSize = baseFont;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }

        // ── 定时刷新 ──────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            var now = DateTime.Now;
            CurrentTimeTb.Text = now.ToString("HH:mm:ss");

            var cur = _manager.GetCurrentExamSubject(now);
            if (cur.HasValue)
            {
                var (exam, subject) = cur.Value;
                ShowCurrentSubject(exam, subject, now);
            }
            else
            {
                // 不在考试中，显示等待或结束
                var next = _manager.GetNextExamSubject(now);
                if (next.HasValue)
                {
                    var (exam, subject) = next.Value;
                    ExamNameTb.Text     = exam.Name;
                    SubjectTb.Text      = subject.Name;
                    var startDt         = now.Date + subject.StartTime;
                    var remaining       = startDt - now;
                    CountdownTb.Text    = remaining > TimeSpan.Zero
                                          ? $"距开考 {remaining:hh\\:mm\\:ss}"
                                          : "--:--";
                    CountdownTb.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xCC));
                    ExamProgress.Value  = 0;
                    ProgressPctTb.Text  = string.Empty;
                    StartTimeTb.Text    = subject.StartTimeStr;
                    EndTimeTb.Text      = subject.EndTimeStr;
                    DurationTb.Text     = $"共 {subject.Duration.TotalMinutes:F0} 分钟";
                    NextSubjectTb.Text  = string.Empty;
                    WarningTb.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ExamNameTb.Text     = "今日考试";
                    SubjectTb.Text      = "考试已结束";
                    CountdownTb.Text    = "00:00";
                    CountdownTb.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    ExamProgress.Value  = 100;
                    ProgressPctTb.Text  = "100%";
                    NextSubjectTb.Text  = string.Empty;
                    WarningTb.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ShowCurrentSubject(ExamEntry exam, ExamSubject subject, DateTime now)
        {
            ExamNameTb.Text = exam.Name;
            SubjectTb.Text  = subject.Name;
            StartTimeTb.Text = subject.StartTimeStr;
            EndTimeTb.Text   = subject.EndTimeStr;
            DurationTb.Text  = $"共 {subject.Duration.TotalMinutes:F0} 分钟";

            // 剩余时间
            var endDt     = now.Date + subject.EndTime;
            var remaining = endDt - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            CountdownTb.Text = remaining.ToString(@"mm\:ss");

            // 颜色随时间变化（沉稳配色）
            CountdownTb.Foreground = remaining.TotalMinutes <= 5
                ? new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x00))
                : remaining.TotalMinutes <= 15
                    ? new SolidColorBrush(Color.FromRgb(0xCC, 0x88, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

            // 进度条
            var elapsed = now - (now.Date + subject.StartTime);
            double pct  = subject.Duration.TotalSeconds > 0
                          ? Math.Clamp(elapsed.TotalSeconds / subject.Duration.TotalSeconds, 0, 1)
                          : 0;
            ExamProgress.Value = pct * 100;
            ProgressPctTb.Text = $"{pct * 100:F1}% 已完成";

            // 下一场
            var next = _manager.GetNextExamSubject(now);
            if (next.HasValue)
            {
                var (_, ns) = next.Value;
                NextSubjectTb.Text = $"下一场：{ns.Name}  {ns.StartTimeStr}";
            }
            else
            {
                NextSubjectTb.Text = string.Empty;
            }

            // 15 分钟警告
            if (remaining.TotalMinutes <= 15 && !_warnShown)
            {
                _warnShown = true;
                WarningTb.Visibility = Visibility.Visible;
                StartFlash();
            }
            // 科目切换后重置警告
            if (subject.Name != _currentSubjectName)
            {
                _currentSubjectName = subject.Name;
                _warnShown = false;
                _flashRunning = false;
                WarningTb.Visibility = Visibility.Collapsed;
            }
        }

        // ── 闪烁动画 ──────────────────────────────────────────
        private void StartFlash()
        {
            if (_flashRunning) return;
            _flashRunning = true;

            var anim = new DoubleAnimation(0.3, 1.0, new Duration(TimeSpan.FromMilliseconds(600)))
            {
                AutoReverse  = true,
                RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(5)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            WarningTb.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // ── 天气加载 ──────────────────────────────────────────
        public async System.Threading.Tasks.Task LoadWeatherAsync()
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

                string rCity    = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                string district = root.TryGetProperty("district", out var d) ? d.GetString() ?? "" : "";
                string weather  = root.TryGetProperty("weather", out var w) ? w.GetString() ?? "" : "";
                string wIcon    = root.TryGetProperty("weather_icon", out var wi) ? wi.GetString() ?? "" : "";
                int temperature = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number
                    ? (int)t.GetDouble() : 0;

                string location = !string.IsNullOrWhiteSpace(district) ? district
                    : !string.IsNullOrWhiteSpace(rCity) ? rCity : city;

                await Dispatcher.InvokeAsync(() =>
                {
                    // 应用天气字体大小
                    double weatherFs = _settings.WeatherFontSize;
                    if (weatherFs <= 0) weatherFs = 14;
                    W2IconTb.FontSize = weatherFs * 1.0;
                    W2CityTb.FontSize = weatherFs * 0.86;
                    W2WeatherTb.FontSize = weatherFs * 0.86;
                    W2TempTb.FontSize = weatherFs * 0.93;

                    // 应用天气颜色
                    W2CityTb.Foreground = ParseColor(_settings.WeatherCityColor, "#FFFFFFFF");
                    W2WeatherTb.Foreground = ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    W2TempTb.Foreground = ParseColor(_settings.WeatherTempColor, "#FFFF8844");
                    W2IconTb.Foreground = ParseColor(_settings.WeatherIconColor, "#FFFFAA00");

                    W2IconTb.Text = GetWeatherEmoji(wIcon);
                    W2CityTb.Text = location;
                    W2WeatherTb.Text = weather;
                    W2TempTb.Text = $"{temperature}°";
                    WeatherRow2.Visibility = Visibility.Visible;
                });
            }
            catch { }
        }

        private void StartWeatherTimer()
        {
            _weatherTimer?.Stop();
            int intervalMin = _settings.WeatherRefreshInterval;
            if (intervalMin <= 0) return;
            _weatherTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMin)
            };
            _weatherTimer.Tick += async (_, _) => await LoadWeatherAsync();
            _weatherTimer.Start();
        }

        private static string GetWeatherEmoji(string? iconCode)
        {
            return iconCode switch
            {
                "100" => "☀", "101" => "🌤", "102" => "⛅",
                "103" => "⛅", "104" => "☁", "200" => "🌦",
                "300" => "🌧", "301" => "⛈", "400" => "❄",
                "500" => "🌫", _     => "🌤"
            };
        }
        private static SolidColorBrush ParseColor(string hex, string fallback)
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    !string.IsNullOrWhiteSpace(hex) ? hex : fallback));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            }
        }
    }
}
