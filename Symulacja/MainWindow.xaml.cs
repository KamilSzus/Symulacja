using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
                FileSizeMB = new() { customFileSize , 10, 1000},
                EntryTime = DateTime.Now,
                Progress = 0
            };
            customFile.FileSizeMB = new ObservableCollection<int>(customFile.FileSizeMB.OrderBy(file => file));
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
                        var fileInfo = new FileInfo { FileName = $"Client {nextClient.ClientID} - {nextClient.FileSizeMB[0]} MB" };

                        // Dodaj plik do folderu
                        await Application.Current.Dispatcher.InvokeAsync(() => Folders[folderIndex].Add(fileInfo));

                        // Przetwarzanie pliku
                        while (nextClient.Progress < 100 && !token.IsCancellationRequested)
                        {
                            await Task.Delay(100, token);
                            var increment = CalculateIncrement(nextClient.FileSizeMB[0]);
                            nextClient.Progress = Math.Min(nextClient.Progress + increment, 100);
                            fileInfo.Progress = Math.Min(fileInfo.Progress + increment, 100);

                            // Aktualizuj postęp klienta w interfejsie
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                nextClient.OnPropertyChanged(nameof(nextClient.Progress));
                            });
                        }

                        // Usuń plik z folderu po przetworzeniu
                        await Application.Current.Dispatcher.InvokeAsync(() => Folders[folderIndex].Remove(fileInfo));

                        // Usuń pierwszy plik z listy FileSizeMB
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (nextClient.FileSizeMB.Count > 0)
                            {
                                nextClient.FileSizeMB.RemoveAt(0);
                                nextClient.OnPropertyChanged(nameof(nextClient.FileSizeMB));
                            }

                            // Jeśli klient ma więcej plików, oblicz priorytet i dodaj go z powrotem do kolejki
                            if (nextClient.FileSizeMB.Count > 0)
                            {
                                nextClient.Progress = 0;
                                nextClient.EntryTime = DateTime.Now;
                                nextClient.Priority = CalculatePriority(nextClient);
                                processingQueue.Enqueue(nextClient);
                            }
                            else
                            {
                                Application.Current.Dispatcher.InvokeAsync(() => Clients.Remove(nextClient));
                            }
                        });
                    }
                    else
                    {
                        await Task.Delay(100, token); // Zapobiega intensywnemu pętlowaniu
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
            double sizePart = (double)queueLength / client.FileSizeMB[0];
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
        private ObservableCollection<int> _fileSizeMB = new();

        public int ClientID { get; set; }
        public DateTime EntryTime { get; set; }

        public ObservableCollection<int> FileSizeMB
        {
            get => _fileSizeMB;
            set
            {
                if (_fileSizeMB != value)
                {
                    _fileSizeMB = value;
                    OnPropertyChanged(nameof(FileSizeMB));
                }
            }
        }
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

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FileInfo : INotifyPropertyChanged
    {
        private int _progress;

        public string FileName { get; set; }
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
