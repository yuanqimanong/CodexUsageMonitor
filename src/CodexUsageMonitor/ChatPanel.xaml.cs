using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor
{
    public partial class ChatPanel : UserControl, IDisposable
    {
        public const double CardHeight = 124;
        private const double CardSpacing = 10;
        private const int MaximumVisibleCards = 5;
        private readonly DispatcherTimer timer;
        private readonly BackgroundWorker worker;
        private readonly ObservableCollection<SessionCard> cards = new ObservableCollection<SessionCard>();
        private LocalSessionReader reader;
        private bool disposed;
        private bool dialogOpen;
        private int generation;

        public string DataDirectory { get; private set; }
        public bool IsInteracting { get { return dialogOpen; } }
        public event EventHandler PreferencesChanged;
        public event EventHandler ContentHeightChanged;

        public double PreferredHeight
        {
            get
            {
                // Leave room for per-row pixel rounding at fractional display scales.
                double listHeight = cards.Count == 0 ? 120 : Math.Min(cards.Count, MaximumVisibleCards) * (CardHeight + CardSpacing) + 4;
                return ContentLayout.Margin.Top + ContentLayout.Margin.Bottom +
                    ContentLayout.RowDefinitions[0].Height.Value + listHeight + ContentLayout.RowDefinitions[2].Height.Value;
            }
        }

        public ChatPanel()
        {
            InitializeComponent();
            SessionCards.ItemsSource = cards;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += delegate { RefreshCatalog(); };
            worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs args)
            {
                ReadRequest request = (ReadRequest)args.Argument;
                request.Result = request.Reader.Read();
                args.Result = request;
            };
            worker.RunWorkerCompleted += ReadCompleted;
            IsVisibleChanged += delegate
            {
                if (IsVisible && !disposed) { timer.Start(); RefreshCatalog(); }
                else timer.Stop();
            };
        }

        public void Configure(string directory)
        {
            DataDirectory = string.IsNullOrWhiteSpace(directory) ? LocalSessionReader.DefaultHome() : directory;
            reader = new LocalSessionReader(DataDirectory);
            generation++;
            cards.Clear();
            UpdateContentHeight();
        }

        public void RefreshCatalog()
        {
            if (disposed || !IsVisible || reader == null || worker.IsBusy) return;
            worker.RunWorkerAsync(new ReadRequest { Reader = reader, Generation = generation });
        }

        private void ReadCompleted(object sender, RunWorkerCompletedEventArgs args)
        {
            if (disposed) return;
            if (args.Error != null)
            {
                ShowError("暂时无法读取会话，将自动重试。");
                return;
            }
            ReadRequest request = (ReadRequest)args.Result;
            if (request.Generation != generation) { RefreshCatalog(); return; }
            if (request.Result.Error != null) { ShowError(request.Result.Error); return; }

            var running = request.Result.Sessions;
            foreach (SessionCard card in cards.Where(card => !running.Any(item => item.Session.Id == card.Id)).ToList())
                cards.Remove(card);
            foreach (RunningSession item in running)
            {
                SessionCard card = cards.FirstOrDefault(existing => existing.Id == item.Session.Id);
                if (card == null) { card = new SessionCard { Id = item.Session.Id }; cards.Add(card); }
                card.Update(item);
            }
            SessionCount.Text = "正在运行 · " + cards.Count;
            EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = "还没有正在运行的会话";
            EmptyHint.Text = "在 Codex 中发送一条消息，卡片会自动出现在这里。";
            UpdateContentHeight();
        }

        private void ShowError(string message)
        {
            cards.Clear();
            SessionCount.Text = "正在运行";
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitle.Text = "暂时读不到会话";
            EmptyHint.Text = message;
            UpdateContentHeight();
        }

        private void UpdateContentHeight()
        {
            EventHandler handler = ContentHeightChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void ChooseDirectory_Click(object sender, RoutedEventArgs args)
        {
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择包含 state_*.sqlite 和 sessions 的 Codex 数据目录";
                dialog.SelectedPath = DataDirectory;
                dialog.ShowNewFolderButton = false;
                Forms.DialogResult choice;
                dialogOpen = true;
                try { choice = dialog.ShowDialog(); }
                finally { dialogOpen = false; }
                if (choice != Forms.DialogResult.OK) return;
                Configure(dialog.SelectedPath);
                EventHandler handler = PreferencesChanged;
                if (handler != null) handler(this, EventArgs.Empty);
                RefreshCatalog();
            }
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop();
        }

        private sealed class ReadRequest
        {
            public LocalSessionReader Reader;
            public int Generation;
            public SessionReadResult Result;
        }
    }

    internal sealed class SessionCard : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; private set; }
        public string Detail { get; private set; }
        public string Speed { get; private set; }
        public string Input { get; private set; }
        public string Cached { get; private set; }
        public string Output { get; private set; }
        public string Reasoning { get; private set; }
        public string InputExact { get; private set; }
        public string CachedExact { get; private set; }
        public string OutputExact { get; private set; }
        public string ReasoningExact { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public void Update(RunningSession running)
        {
            SessionMetrics metrics = running.Metrics;
            SpeedSample latest = metrics.Samples.LastOrDefault();
            Title = running.Session.Title;
            Detail = (metrics.Model ?? running.Session.Model ?? "模型未记录") + " · " + metrics.State +
                (latest == null ? "" : " · " + latest.Timestamp.ToLocalTime().ToString("HH:mm:ss"));
            Speed = latest != null && latest.TokensPerSecond.HasValue ? latest.TokensPerSecond.Value.ToString("0.0") : "—";
            TokenUsage total = metrics.Total ?? new TokenUsage();
            Input = Compact(total.Input);
            Cached = Compact(total.Cached);
            Output = Compact(total.Output);
            Reasoning = Compact(total.Reasoning);
            InputExact = Exact(total.Input);
            CachedExact = Exact(total.Cached) + "，包含在输入中";
            OutputExact = Exact(total.Output);
            ReasoningExact = Exact(total.Reasoning) + "，包含在输出中";
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(string.Empty));
        }

        private static string Exact(long? count)
        {
            return count.HasValue ? count.Value.ToString("N0") + " Token" : "日志未记录";
        }

        private static string Compact(long? count)
        {
            if (!count.HasValue) return "—";
            if (count >= 1000000) return (count.Value / 1000000.0).ToString("0.##") + "M";
            if (count >= 10000) return (count.Value / 1000.0).ToString("0.#") + "k";
            return count.Value.ToString("N0");
        }
    }
}
