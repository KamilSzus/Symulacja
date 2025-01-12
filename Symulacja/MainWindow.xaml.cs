using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Symulacja
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ClientInfo> Clients { get; set; }
        public ObservableCollection<FileInfo>[] Folders { get; set; }

        private ConcurrentQueue<ClientInfo> processingQueue;
        private SemaphoreSlim semaphore;
        private CancellationTokenSource cancellationTokenSource;
        private int nextClientID;

        public MainWindow()
        {
            InitializeComponent();
            Clients = new ObservableCollection<ClientInfo>();
            Folders = new ObservableCollection<FileInfo>[5]
            {
                new ObservableCollection<FileInfo>(),
                new ObservableCollection<FileInfo>(),
                new ObservableCollection<FileInfo>(),
                new ObservableCollection<FileInfo>(),
                new ObservableCollection<FileInfo>()
            };

            processingQueue = new ConcurrentQueue<ClientInfo>();
            semaphore = new SemaphoreSlim(5); // One thread per folder
            nextClientID = 1;

            DataContext = this;
        }

        private async void StartSimulation(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource = new CancellationTokenSource();

            foreach (var client in Clients)
            {
                processingQueue.Enqueue(client);
                client.Priority = CalculatePriority(client);
            }

            var updateTask = UpdatePrioritiesAsync(cancellationTokenSource.Token);
            var processingTasks = Enumerable.Range(0, 5).Select(index => ProcessFolderAsync(index, cancellationTokenSource.Token)).ToArray();

            await Task.WhenAll(updateTask, Task.WhenAll(processingTasks));
        }

        private void StopSimulation(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
        }

        private async void AddCustomFile(object sender, RoutedEventArgs e)
        {
            int customFileSize;

            if (string.IsNullOrEmpty(FileSizeTextBox.Text) || !int.TryParse(FileSizeTextBox.Text, out customFileSize) || customFileSize <= 0)
            {
                Random random = new Random();
                customFileSize = random.Next(1, 100000); // Random file size between 1 and 100000 MB
            }

            var customFile = new ClientInfo
            {
                ClientID = nextClientID++,
                FileSizeMB = customFileSize,
                EntryTime = DateTime.Now,
                Progress = 0
            };

            customFile.Priority = CalculatePriority(customFile);
            await Application.Current.Dispatcher.InvokeAsync(() => Clients.Add(customFile));
            processingQueue.Enqueue(customFile);
        }

        private async Task ProcessFolderAsync(int folderIndex, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await semaphore.WaitAsync(token);

                try
                {
                    var nextClient = GetNextClientByPriority();
                    if (nextClient != null)
                    {
                        var fileInfo = new FileInfo { FileName = $"Client {nextClient.ClientID} - {nextClient.FileSizeMB} MB" };

                        // Add file to the folder
                        await Application.Current.Dispatcher.InvokeAsync(() => Folders[folderIndex].Add(fileInfo));

                        // Process the file
                        while (nextClient.Progress < 100 && !token.IsCancellationRequested)
                        {
                            await Task.Delay(100, token);
                            nextClient.Progress += CalculateIncrement(nextClient.FileSizeMB);

                            if (nextClient.Progress > 100)
                                nextClient.Progress = 100;
                        }

                        // Remove file from folder after processing
                        await Application.Current.Dispatcher.InvokeAsync(() => Folders[folderIndex].Remove(fileInfo));

                        // Remove client from the queue and UI
                        await Application.Current.Dispatcher.InvokeAsync(() => Clients.Remove(nextClient));
                    }
                    else
                    {
                        await Task.Delay(100, token); // Prevent tight loop
                    }
                }
                finally
                {
                    semaphore.Release();
                }
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

        private double CalculatePriority(ClientInfo client)
        {
            // Priority formula: log(base queue length)(time in queue) + (queue length / file size)
            int queueLength = Math.Max(processingQueue.Count + 1, 1); // Avoid division by zero
            double timeInQueue = Math.Max((DateTime.Now - client.EntryTime).TotalSeconds, 1);
            double logPart = Math.Log(timeInQueue, queueLength);
            double sizePart = (double)queueLength / client.FileSizeMB;
            return logPart + sizePart;
        }

        private int CalculateIncrement(int fileSize)
        {
            return Math.Max(1, 500 / fileSize);
        }

        private void DataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

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

    public class FileInfo //: INotifyPropertyChanged
    {
      //  private int _progress;

        public string FileName { get; set; }
     //   public int Progress
     //   {
     //       get => _progress;
     //       set
     //       {
     //           if (_progress != value)
     //           {
     //               _progress = value;
     //               OnPropertyChanged(nameof(Progress));
     //           }
     //       }
     //   }

     // //  public event PropertyChangedEventHandler PropertyChanged;
     //
     //   protected void OnPropertyChanged(string propertyName)
     //   {
     //       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
     //   }

    }
}
