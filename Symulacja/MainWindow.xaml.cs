using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Symulacja
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<ClientInfo> Clients { get; set; }
        private ConcurrentQueue<ClientInfo> processingQueue;
        private SemaphoreSlim semaphore;
        private CancellationTokenSource cancellationTokenSource;
        private int maxConcurrentFiles;
        private int nextClientID;

        public MainWindow()
        {
            InitializeComponent();
            Clients = new ObservableCollection<ClientInfo>();
            processingQueue = new ConcurrentQueue<ClientInfo>();
            DataContext = this;

            // Set maximum concurrent files
            maxConcurrentFiles = 3; // Example value, adjust as needed
            semaphore = new SemaphoreSlim(maxConcurrentFiles);

            // Initialize next client ID
            nextClientID = 1;
        }

        private async void StartSimulation(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource = new CancellationTokenSource();

            foreach (var client in Clients)
            {
                processingQueue.Enqueue(client);
                client.Priority = CalculatePriority(client);
            }

            _ = Task.Run(() => UpdatePrioritiesAsync(cancellationTokenSource.Token));
            await ProcessQueueAsync(cancellationTokenSource.Token);
        }

        private void StopSimulation(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
        }

        private void AddFile(object sender, RoutedEventArgs e)
        {
            var newFile = new ClientInfo
            {
                ClientID = nextClientID++,
                FileSizeMB = new Random().Next(10, 500),
                EntryTime = DateTime.Now,
                Progress = 0
            };

            newFile.Priority = CalculatePriority(newFile);
            Clients.Add(newFile);
            processingQueue.Enqueue(newFile);
        }

        private void AddCustomFile(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FileSizeTextBox.Text, out int customFileSize) && customFileSize > 0)
            {
                var customFile = new ClientInfo
                {
                    ClientID = nextClientID++,
                    FileSizeMB = customFileSize,
                    EntryTime = DateTime.Now,
                    Progress = 0
                };

                customFile.Priority = CalculatePriority(customFile);
                Clients.Add(customFile);
                processingQueue.Enqueue(customFile);
            }
            else if (string.IsNullOrEmpty(FileSizeTextBox.Text))
            {
                Random r = new Random(DateTime.Now.Microsecond);
                int rInt = r.Next(1, 100000);

                var customFile = new ClientInfo
                {
                    ClientID = nextClientID++,
                    FileSizeMB = rInt,
                    EntryTime = DateTime.Now,
                    Progress = 0
                };

                customFile.Priority = CalculatePriority(customFile);
                Clients.Add(customFile);
                processingQueue.Enqueue(customFile);
            }
            else
            {
                MessageBox.Show("Please enter a valid file size (positive integer).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var nextClient = GetNextClientByPriority();

                if (nextClient == null)
                {
                    await Task.Delay(100, token); // Wait for new files
                    continue;
                }

                await semaphore.WaitAsync(token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessClientAsync(nextClient, token);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, token);

                await Task.Delay(100, token); // Throttle to reduce UI contention
            }
        }

        private ClientInfo GetNextClientByPriority()
        {
            lock (processingQueue)
            {
                var sortedClients = processingQueue.ToList().OrderByDescending(c => c.Priority).ToList();
                foreach (var client in sortedClients)
                {
                    if (client.Progress < 100)
                    {
                        processingQueue = new ConcurrentQueue<ClientInfo>(sortedClients.Where(c => c != client));
                        return client;
                    }
                }
            }
            return null;
        }

        private async Task ProcessClientAsync(ClientInfo client, CancellationToken token)
        {
            while (client.Progress < 100 && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // Simulate work

                client.Progress += CalculateIncrement(client.FileSizeMB);
                if (client.Progress > 100)
                {
                    client.Progress = 100;
                }
            }

            if (client.Progress >= 100)
            {
                Application.Current.Dispatcher.Invoke(() => Clients.Remove(client));
            }
        }

        private async Task UpdatePrioritiesAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var client in Clients)
                {
                    client.Priority = CalculatePriority(client);
                }
                await Task.Delay(500, token); // Update priorities every 500ms
            }
        }

        private double CalculatePriority(ClientInfo client)
        {
            // Formula: log base queue length (time in queue) + (queue length / file size)
            int queueLength = Math.Max(processingQueue.Count, 1); // Avoid division by zero
            double timeInQueue = Math.Max((DateTime.Now - client.EntryTime).TotalSeconds, 1);
            double logPart = Math.Log(timeInQueue, queueLength);
            double sizePart = (double)queueLength / client.FileSizeMB;
            return logPart + sizePart;
        }

        private int CalculateIncrement(int fileSize)
        {
            // Example formula: Progress increment based on file size
            return Math.Max(1, 500 / fileSize); // Adjust constants as needed
        }
    }

    public class ClientInfo : INotifyPropertyChanged
    {
        private int _progress;
        private double _priority;

        public int ClientID { get; set; }
        public int FileSizeMB { get; set; }
        public DateTime EntryTime { get; set; }

        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public double Priority
        {
            get => _priority;
            set
            {
                if (Math.Abs(_priority - value) > 0.01)
                {
                    _priority = value;
                    OnPropertyChanged(nameof(Priority));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
