using System.Windows;

namespace WPFUI.Utilities
{
    public partial class ProgressDialog : Window
    {
        private static ProgressDialog _instance;
        private static Thread _thread;
        private static bool _isClosing = false;
        private static string _pendingTitle;

        public ProgressDialog()
        {
            InitializeComponent();
        }

        public static void ShowProgress(int current, int total, string message = null, string detail = null, string title = null)
        {
            if (_isClosing)
            {
                Thread.Sleep(200);
                _isClosing = false;
            }

            if (_instance == null || _thread == null || !_thread.IsAlive)
            {
                _isClosing = false;
                _instance = null;
                _thread = null;
                _pendingTitle = title;

                _thread = new Thread(() =>
                {
                    EnsureApplicationInitialized();
                    _instance = new ProgressDialog();
                    if (!string.IsNullOrEmpty(_pendingTitle))
                        _instance.Title = _pendingTitle;
                    _instance.Show();
                    System.Windows.Threading.Dispatcher.Run();
                });
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();

                Thread.Sleep(200);
            }

            if (_instance != null && _instance.Dispatcher != null && !_isClosing)
            {
                try
                {
                    _instance.Dispatcher.Invoke(() =>
                    {
                        if (_instance != null && !_isClosing)
                        {
                            var percentage = total == 0 ? 0 : (current * 100.0) / total;
                            _instance.ProgressBar.Value = percentage;
                            _instance.ProgressPercentText.Text = $"{percentage:F1}%";

                            if (!string.IsNullOrEmpty(message))
                            {
                                string cleanMsg = System.Text.RegularExpressions.Regex.Replace(message, @"\s*\b\d+\s*/\s*\d+\b", "");
                                _instance.ProgressTextBlock.Text = cleanMsg.Trim();
                            }
                            else
                            {
                                _instance.ProgressTextBlock.Text = "Processing...";
                            }

                            if (!string.IsNullOrEmpty(detail))
                                _instance.DetailTextBlock.Text = detail;
                        }
                    });
                }
                catch
                {
                    _instance = null;
                    _thread = null;
                    _isClosing = false;
                }
            }
        }

        public static void CloseProgress()
        {
            _isClosing = true;

            if (_instance != null && _instance.Dispatcher != null)
            {
                try
                {
                    _instance.Dispatcher.Invoke(() =>
                    {
                        _instance?.Close();
                    });
                }
                catch
                {
                }
            }

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(1000);
            }

            _instance = null;
            _thread = null;
            _isClosing = false;
        }

        private static void EnsureApplicationInitialized()
        {
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }
}
