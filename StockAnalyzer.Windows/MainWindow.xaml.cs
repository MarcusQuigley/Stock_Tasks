using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Newtonsoft.Json;
using StockAnalyzer.Core.Domain;
using StockAnalyzer.Windows.Services;

namespace StockAnalyzer.Windows
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        CancellationTokenSource cancellationTokenSource = null;

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            #region Before loading stock data
            var watch = new Stopwatch();
            watch.Start();
            StockProgress.Visibility = Visibility.Visible;
            StockProgress.IsIndeterminate = true;

            Search.Content = "Cancel";
            #endregion

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
                return;
            }
            cancellationTokenSource = new CancellationTokenSource();
            var loadedLinesTask = SearchForStocks(cancellationTokenSource.Token);

            loadedLinesTask.ContinueWith<List<StockPrice>>(antecendent =>
            {
                
                var data = new List<StockPrice>();

                foreach (var line in antecendent.Result.Skip(1))
                {
                    var segments = line.Split(',');

                    for (var i = 0; i < segments.Length; i++) segments[i] = segments[i].Trim('\'', '"');
                    var price = new StockPrice
                    {
                        Ticker = segments[0],
                        TradeDate = DateTime.ParseExact(segments[1], "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
                        Volume = Convert.ToInt32(segments[6], CultureInfo.InvariantCulture),
                        Change = Convert.ToDecimal(segments[7], CultureInfo.InvariantCulture),
                        ChangePercent = Convert.ToDecimal(segments[8], CultureInfo.InvariantCulture),
                    };
                    data.Add(price);
                }
                return data;
            },TaskContinuationOptions.OnlyOnRanToCompletion).ContinueWith(prices =>
            {
                Action action = () => Stocks.ItemsSource = prices.Result.Where(price => price.Ticker == Ticker.Text.ToUpper());
                Dispatcher.BeginInvoke(action);
                return prices.Result.Count;

            },  TaskContinuationOptions.OnlyOnRanToCompletion
            ).ContinueWith(count=>
            {
                Action action = () =>
                {
                    #region After stock data is loaded
                    StocksStatus.Text = $"Loaded stocks for {Ticker.Text} in {watch.ElapsedMilliseconds}ms, loaded {count.Result} lines";
                    StockProgress.Visibility = Visibility.Hidden;
                    Search.Content = "Search";
                    #endregion
                };
                Dispatcher.BeginInvoke(action);
            });

            loadedLinesTask.ContinueWith(antecendent =>
           {
               Action action = () => Notes.Text += antecendent.Exception.InnerException.Message;
               Dispatcher.BeginInvoke(action);

           }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async void Search_Multiple_Click(object sender, RoutedEventArgs e)
        {
            #region Before loading stock data
            var watch = new Stopwatch();
            watch.Start();
            StockProgress.Visibility = Visibility.Visible;
            StockProgress.IsIndeterminate = true;

            SearchMulti.Content = "Cancel";
            #endregion

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
                return;
            }
            cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Token.Register(() =>
            {
                Notes.Text += "Cancellation requested" + Environment.NewLine;
            });

            try
            {

                var tickers = Ticker.Text.ToUpper().Split(',', ' ');
                var service = new StockService();
                var stocks = new ConcurrentBag<StockPrice>();

                var tickerLoadingTasks = new List<Task<IEnumerable<StockPrice>>>();
                foreach (var ticker in tickers)
                {
                    var loadTask = service.GetStockPricesFor(ticker);
                    tickerLoadingTasks.Add(loadTask);
                }
                var timeOutTask = Task.Delay(2000);
                var resultTask = Task.WhenAll(tickerLoadingTasks);
                var completedTask = await Task.WhenAny(timeOutTask, resultTask);


                if (completedTask == timeOutTask)
                {
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource = null;
                    throw new Exception("Timeout");
                }
                     Stocks.ItemsSource = resultTask.Result.SelectMany(stock => stock);
             }
            catch (Exception ex)
            {
                Notes.Text += ex.Message + Environment.NewLine;
            }

            finally
            {
                cancellationTokenSource = null;
            }

            #region After stock data is loaded
            StocksStatus.Text = $"Loaded stocks for {Ticker.Text} in {watch.ElapsedMilliseconds}ms";
            StockProgress.Visibility = Visibility.Hidden;
            SearchMulti.Content = "Search Multi";
            #endregion

        }

        private Task<List<string>> SearchForStocks(CancellationToken cancellationToken)
        {
            var loadLinesTask = Task.Run(async () =>
            {
                var lines = new List<string>();

                using (var stream = new StreamReader(File.OpenRead(@"StockPrices_small.csv")))
                {
                    string line;
                    while ((line = await stream.ReadLineAsync()) != null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return lines;
                        }
                        lines.Add(line);
                    }
                }

                return lines;
            }, cancellationToken);

            return loadLinesTask;
        }

        private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));

            e.Handled = true;
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
