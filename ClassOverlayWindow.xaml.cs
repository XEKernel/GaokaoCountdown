using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    /// <summary>
    /// 下课倒计时大字覆盖层 — 屏幕居中显示，3秒后自动消失
    /// </summary>
    public partial class ClassOverlayWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;
        private bool _isClosing;

        private ClassOverlayWindow(string mainText, string subText, bool autoClose = true)
        {
            InitializeComponent();
            MainText.Text = mainText;
            SubText.Text = subText;

            if (!autoClose)
                _autoCloseTimer = null!; // won't use
            else
            {
                _autoCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _autoCloseTimer.Tick += (_, _) =>
                {
                    _autoCloseTimer.Stop();
                    CloseWithAnimation();
                };
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 入场动画：缩放 + 淡入
            var scaleXAnim = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleYAnim = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            BeginAnimation(OpacityProperty, opacityAnim);

            _autoCloseTimer?.Start();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            CloseWithAnimation();
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            var scaleXAnim = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleYAnim = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            opacityAnim.Completed += (_, _) => Close();

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            BeginAnimation(OpacityProperty, opacityAnim);
        }

        // ══════════════════════════════════════════════════════
        //  静态方法 — 主窗口调用
        // ══════════════════════════════════════════════════════

        /// <summary>距下课倒计时（每秒刷新）</summary>
        public static ClassOverlayWindow? ShowCountdown(int remainingSeconds)
        {
            string main = remainingSeconds > 10
                ? $"还有 {remainingSeconds} 秒"
                : $"还有 {remainingSeconds} 秒";
            string sub = remainingSeconds > 10 ? "距下课" : "快要下课了";

            var win = new ClassOverlayWindow(main, sub, autoClose: false);
            win.Show();
            return win;
        }

        /// <summary>显示"下课"大字，3 秒后消失</summary>
        public static void ShowClassEnd()
        {
            var win = new ClassOverlayWindow("下课！", "休息一下吧", autoClose: true);
            win.Show();
        }

        /// <summary>更新已有窗口的倒计时数字</summary>
        public void UpdateCountdown(int remainingSeconds)
        {
            if (_isClosing) return;
            string main = remainingSeconds > 10
                ? $"还有 {remainingSeconds} 秒"
                : $"还有 {remainingSeconds} 秒";
            string sub = remainingSeconds > 10 ? "距下课" : "快要下课了";

            MainText.Text = main;
            SubText.Text = sub;
        }
    }
}
