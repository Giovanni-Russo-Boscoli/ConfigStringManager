using Microsoft.Data.SqlClient;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brush;
using System.Text;
using System.Net;
using System.Windows.Threading;
using System.Windows.Forms;

namespace ConfigStringManager
{
    public partial class MainWindow : Window
    {
        private readonly string userFolder = "ConfigStringManagerSetup";
        private readonly string appFolder;
        private readonly string aliasesPath;
        private readonly string envFileName = "bomiEnvironments.json";
        
        public ObservableCollection<DevEnvironment> DevEnvs { get; } = new();

        private List<ServerEntry> servers = new();
        private static readonly HashSet<string> ignoreKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "DH2CRSCodesServer", // from STS.Web.config
            "PerformanceEntities" // from Sdm.App.PubSub.Web.config & Sdm.App.Web.config
        };

        private DevEnvironment currentDevEnv = null;
        private AliasItem currentAlias = null;
        private (string name, XElement element) currentConn = (null, null);

        private CancellationTokenSource _dbLoadCts;
        private bool _suppressServerComboEvent = false;
        //private readonly DispatcherTimer _statusTimer;

        private readonly Dictionary<string, List<string>> _databaseCache = new(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), userFolder);
            Directory.CreateDirectory(appFolder);
            aliasesPath = Path.Combine(appFolder, envFileName);

            //_statusTimer = new DispatcherTimer(); _statusTimer.Tick += (s, e) => { StatusMessage.Visibility = Visibility.Collapsed; _statusTimer.Stop(); };

            servers = LoadServers();

            LoadAliases();

            RefreshUI();
        }

        private static Task DoEventsAsync()
        {
            return Application.Current.Dispatcher.InvokeAsync(() => { },
                System.Windows.Threading.DispatcherPriority.Background).Task;
        }

        #region Load/Save

        private List<ServerEntry> LoadServers()
        {
            string filePath = GetServersFilePath();

            // If file doesn't exist, create with defaults
            if (!File.Exists(filePath))
            {
                var defaults = GetDefaultServers();
                SaveServers(defaults);
                return defaults;
            }

            // Try to load existing file
            try
            {
                string json = File.ReadAllText(filePath);
                var servers = JsonSerializer.Deserialize<List<ServerEntry>>(json);
                List<ServerEntry> _servers = new List<ServerEntry>();
                //string strServers = "";
                //foreach (var item in servers)
                //{
                //    strServers += $@"new ServerEntry {{ Name = ""{item.Name}"" , Address = ""{item.Address.Replace("\\", "##")}"" }},\n";
                //    new ServerEntry { Name = "Server1", Address = "someinstance\\server1" },

                //}

                // If file is empty or invalid, recreate defaults
                if (servers == null || servers.Count == 0)
                {
                    var defaults = GetDefaultServers();
                    SaveServers(defaults);
                    return defaults;
                }

                return servers;
            }
            catch
            {
                // Corrupted file, recreate defaults
                var defaults = GetDefaultServers();
                SaveServers(defaults);
                return defaults;
            }
        }

        private void SaveServers(List<ServerEntry> servers)
        {
            string filePath = GetServersFilePath();
            string json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public void LoadAliases()
        {
            DevEnvs.Clear();

            try
            {
                if (File.Exists(aliasesPath))
                {
                    var json = File.ReadAllText(aliasesPath);
                    var list = JsonSerializer.Deserialize<List<DevEnvironment>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new List<DevEnvironment>();

                    foreach (var env in list)
                        DevEnvs.Add(env);
                }
                else
                {
                    var defaultEnv = new DevEnvironment()
                    {
                        Name = "INTEG MIRROR",
                        PrefixPath = "D:\\YOUR_ENVIRONMENT_PATH\\Trunk\\",
                        STSPrefixPath = "D:\\YOUR_STS_PATH\\Sdm.App.STS\\"
                    };

                    DevEnvs.Add(defaultEnv);
                    SaveAliases();
                }
            }
            catch
            {
                // fallback to empty collection
            }
        }


        private void SaveAliases()
        {
            try
            {
                File.WriteAllText(aliasesPath, JsonSerializer.Serialize(DevEnvs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed saving config list: " + ex.Message);
            }
        }

        #endregion

        #region UI Rendering

        private void RefreshUI(string envVisible = "", bool clearStatusText = true)
        {
            // Ensure each AliasItem has a placeholder child so it shows an expander
            foreach (var env in DevEnvs)
            {
                env.EnsureFilesInitialized();

                foreach (var alias in env.Files)
                {
                    if (alias.Children.Count == 0)
                        alias.Children.Add(new MissingFileMessage { Message = "(expand to load)" });
                }
            }

            FilesTree.ItemsSource = null;
            FilesTree.ItemsSource = DevEnvs;

            if (!string.IsNullOrEmpty(envVisible))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    foreach (var env in DevEnvs)
                    {
                        if (env.Name == envVisible)
                        {
                            var container = (TreeViewItem)FilesTree.ItemContainerGenerator.ContainerFromItem(env);
                            if (container != null)
                                container.IsExpanded = true;
                        }
                    }
                });
            }

            if (clearStatusText)
                ClearSelection();
        }

        private void ClearSelection()
        {
            currentAlias = null;
            currentConn = (null, null);
            ServerCombo.ItemsSource = null;
            DatabaseCombo.ItemsSource = null;
            //clearMessages();
            PanelConnEnabled(false);
        }

        //private void clearMessages()
        //{
        //    //StatusMessage.Text = "";
        //    //StatusText.Text = "";
        //    //StatusTextFileServers.Text = "";
        //}

        private void PanelConnEnabled(bool enabled)
        {
            ServerCombo.IsEnabled = enabled;
            DatabaseCombo.IsEnabled = enabled;
            btnSaveChanges.IsEnabled = enabled;
        }

        #endregion

        #region Tree / file loading

        private void Root_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem root) return;
            if (root.DataContext is not AliasItem alias) return;

            alias.Children.Clear();

            var filePath = GetEnvironmentName(alias);

            if (!File.Exists(filePath))
            {
                alias.Children.Add(new MissingFileMessage
                {
                    Message = $"(File not found)\nCurrently reading from: {filePath}\nCheck file: Desktop\\{userFolder}"
                });
                return;
            }

            try
            {
                var xml = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);

                //WEBCONFIG & PUBSUB WEBCONFIG & STS
                var adds = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase)) &&
                                                        x.Parent != null && string.Equals(x.Parent.Name.LocalName, "connectionStrings", StringComparison.OrdinalIgnoreCase));
                //WEBCONFIG SESSION-STATE
                var addsCustom = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "sessionState", StringComparison.OrdinalIgnoreCase) &&
                                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "sqlConnectionString", StringComparison.OrdinalIgnoreCase)) &&
                                                        x.Parent != null && string.Equals(x.Parent.Name.LocalName, "system.web", StringComparison.OrdinalIgnoreCase));

                //ASMX/BOMI2 & PUBSUB/ASMX/BOMI2
                var addsCustom2 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "key", StringComparison.OrdinalIgnoreCase)) &&
                                                        x.Attribute("key").Value.Equals("DH2SQLServer"));

                //PERFORMANCE - PROFILING (MonitoringConfiguration.xml)
                var addsCustom3 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "ProfileConsumer", StringComparison.OrdinalIgnoreCase) &&
                                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) &&
                                                        (a.Value.Equals("HttpRequestConsumer") || a.Value.Equals("HtmlCachingConsumer") || a.Value.Equals("MemberAccessConsumer"))));

                //LOG4NET
                var addsCustom4 = xml.Descendants("log4net").Where(x => string.Equals(x.Name.LocalName, "log4net", StringComparison.OrdinalIgnoreCase)).Descendants("appender")
                                    .Where(x => x.Attributes().Any(a => a.Name.LocalName.Equals("name") && a.Value.Equals("ErrorHandlerModule_SQLAppender")));

                if (!adds.Any() && !addsCustom.Any() && !addsCustom2.Any() && !addsCustom3.Any() && !addsCustom4.Any())
                {
                    alias.Children.Add(new MissingFileMessage { Message = "(no connection strings found)" });
                    return;
                }

                foreach (var add in adds)
                {
                    var name = add.Attribute("name")?.Value ?? "(unnamed)";
                    if (ignoreKeyList(name)) continue;

                    alias.Children.Add(new ConnectionEntry
                    {
                        Name = name,
                        Alias = alias,
                        Element = add,
                        Foreground = System.Windows.Media.Brushes.Black
                        //FontWeight = FontWeights.UltraBold
                    });
                }

                foreach (var add in addsCustom)
                {
                    alias.Children.Add(new ConnectionEntry
                    {
                        Name = add.Attribute("mode")?.Value ?? "(unnamed)",
                        Alias = alias,
                        Element = add,
                        Foreground = System.Windows.Media.Brushes.DarkGreen,
                        FontWeight = FontWeights.Bold
                    });
                }

                foreach (var add in addsCustom2)
                {
                    alias.Children.Add(new ConnectionEntry
                    {
                        Name = add.Attribute("key")?.Value ?? "(unnamed)",
                        Alias = alias,
                        Element = add,
                        Foreground = System.Windows.Media.Brushes.BlueViolet,
                        FontWeight = FontWeights.Bold
                    });
                }

                foreach (var add in addsCustom3)
                {
                    var name = add.Attribute("name")?.Value ?? "(unnamed)";

                    var descendant = add.Descendants().Where(x => string.Equals(x.Name.LocalName, "Setting", StringComparison.OrdinalIgnoreCase) &&
                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) && a.Value.Equals("ConnectionString"))).FirstOrDefault();

                    alias.Children.Add(new ConnectionEntry
                    {
                        Name = add.Attribute("name")?.Value ?? "(unnamed)",
                        Alias = alias,
                        Element = descendant,
                        Foreground = System.Windows.Media.Brushes.DarkBlue,
                        FontWeight = FontWeights.Bold
                    });
                }

                foreach (var add in addsCustom4)
                {
                    var name = add.Attribute("name")?.Value ?? "(unnamed)";

                    var descendant = add.Descendants()
                        .Where(x => string.Equals(x.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase) &&
                                    x.Attributes().Any(a => string.Equals(a.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault();

                    alias.Children.Add(new ConnectionEntry
                    {
                        Name = name,
                        Alias = alias,
                        Element = descendant,   // IMPORTANT: use descendant, not add
                        Foreground = System.Windows.Media.Brushes.Black,
                        FontWeight = FontWeights.Bold
                    });
                }

            }
            catch (Exception ex)
            {
                alias.Children.Add(new MissingFileMessage
                {
                    Message = $"(error reading file: {ex.Message})"
                });
            }
        }


        private bool ignoreKeyList(string key) { return ignoreKeys.Contains(key); }

        private void Env_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not TreeViewItem env) return;
            if (env.DataContext is not DevEnvironment devEnv) return;

            ClearStatusMessage();
            env.IsExpanded = true;
            currentAlias = null;
            currentDevEnv = devEnv;

            AliasBox.Text = devEnv.Name;
            FilePathText.Visibility = Visibility.Collapsed;
            btnCopyText.Visibility = Visibility.Collapsed;
            lblFile.Visibility = Visibility.Collapsed;
            ConnNameText.Text = string.Empty;

            PanelConnEnabled(false);
            AliasPanel.Visibility = Visibility.Visible;
            ConnStringsPanel.Visibility = Visibility.Collapsed;

            PopulateFileServersCombo();
            //clearMessages();
        }

        private void Root_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not TreeViewItem root) return;
            if (root.DataContext is not AliasItem alias) return;

            ClearStatusMessage();
            currentDevEnv = null;
            currentAlias = alias;

            AliasBox.Text = alias.Alias;
            FilePathText.Visibility = Visibility.Visible;
            btnCopyText.Visibility = Visibility.Visible;
            lblFile.Visibility = Visibility.Visible;
            FilePathText.Text = GetEnvironmentName(alias);

            ConnNameText.Text = string.Empty;

            PanelConnEnabled(false);
            AliasPanel.Visibility = Visibility.Visible;
            ConnStringsPanel.Visibility = Visibility.Collapsed;

            PopulateFileServersCombo();
            //clearMessages();
        }

        private async void Conn_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem item) return;
            if (item.DataContext is not ConnectionEntry entry) return;

            ClearStatusMessage();

            AliasPanel.Visibility = Visibility.Collapsed;

            // Cancel any previous DB load
            _dbLoadCts?.Cancel();
            _dbLoadCts = new CancellationTokenSource();
            var token = _dbLoadCts.Token;

            //StatusTextFileServers.Text = string.Empty;
            DatabaseCombo.ItemsSource = null;

            currentAlias = entry.Alias;
            var element = entry.Element;

            var name = "";
            var raw = "";

            switch (currentAlias.Alias_Enum)
            {
                case AliasEnum.SdmAppWebConfig:
                case AliasEnum.SdmAppPubSubWebConfig:
                case AliasEnum.SdmAppMonitoringConfiguration:
                case AliasEnum.STSWebConfig:
                    name = element.Attribute("name")?.Value
                        ?? element.Attribute("mode")?.Value
                        ?? "";
                    raw = element.Attribute("connectionString")?.Value
                        ?? element.Attribute("sqlConnectionString")?.Value
                        ?? element.Attribute("value")?.Value
                        ?? "";
                    break;

                case AliasEnum.SdmAppBOMi2WebConfig:
                case AliasEnum.SdmAppPubSubBOMi2WebConfig:
                    name = element.Attribute("key")?.Value ?? "";
                    raw = element.Attribute("value")?.Value ?? "";
                    break;

                case AliasEnum.SdmLog4Net:
                    name = "ErrorHandlerModule_SQLAppender";
                    raw = element.Attribute("value")?.Value ?? "";
                    break;
            }

            name = name.Trim();
            currentConn = (name, element);

            AliasBox.Text = currentAlias.Alias;
            FilePathText.Text = GetEnvironmentName(currentAlias);
            ConnNameText.Text = name;

            var parsedServer = ParseConn(raw, new[] { "Server", "Data Source", "Address", "Addr" }).Trim('\'', ' ');
            var parsedDb = ParseConn(raw, new[] { "Database", "Initial Catalog" }).Trim('\'', ' ');

            // 🔥 Prevent ServerCombo_SelectionChanged from firing
            _suppressServerComboEvent = true;

            await PopulateServerComboAsync(parsedServer);

            // Allow UI to update
            await DoEventsAsync();

            // Re-enable event
            _suppressServerComboEvent = false;

            // Now safely load DBs
            await PopulateDatabasesAsync(parsedServer, parsedDb, token);

            PanelConnEnabled(true);
            //StatusText.Text = "";
            AliasPanel.Visibility = Visibility.Collapsed;
            ConnStringsPanel.Visibility = Visibility.Visible;
            //clearMessages();
        }


        #endregion

        #region Server management UI

        private async void BtnCopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(FilePathText.Text);
            //StatusTextFileServers.Text = "Copied!";
            ShowStatusAsync("Copied.", System.Windows.Media.Brushes.DarkRed);
        }

        private void RenderServerComboItems(string fileServer)
        {
            fileServer = fileServer.TrimStart('\'').TrimEnd('\'');
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(fileServer)) list.Add(fileServer);
            foreach (var s in servers)
            {
                if (!list.Any(x => string.Equals(x, s.Address, StringComparison.OrdinalIgnoreCase)))
                    list.Add(s.Address);
            }

            ServerCombo.ItemsSource = list;
            if (!string.IsNullOrWhiteSpace(fileServer)) ServerCombo.SelectedItem = fileServer;
            else if (list.Count > 0) ServerCombo.SelectedIndex = 0;
        }

        private Task PopulateServerComboAsync(string fileServer)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                RenderServerComboItems(fileServer);
            }).Task;
        }

        private void RenderFileServersCombo(string fileServer)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(fileServer)) list.Add(fileServer);
            foreach (var s in servers)
            {
                if (!list.Any(x => string.Equals(x, s.Address, StringComparison.OrdinalIgnoreCase)))
                    list.Add(s.Address);
            }

            FileServersCombo.ItemsSource = list;
            if (!string.IsNullOrWhiteSpace(fileServer)) FileServersCombo.SelectedItem = fileServer;
            else if (list.Count > 0) FileServersCombo.SelectedIndex = 0;
        }

        private void PopulateFileServersCombo()
        {
            Dispatcher.Invoke(() => RenderFileServersCombo(string.Empty));
        }

        private async void BtnUpdateServersOnFile_Click(object sender, RoutedEventArgs e)
        {
            if ((currentAlias == null || AliasBox.Text.Equals(string.Empty)) && (currentDevEnv == null || AliasBox.Text.Equals(string.Empty)))
            {
                //StatusTextFileServers.Text = "No file/alias selected.";
                ShowStatusAsync("No file/alias selected.", System.Windows.Media.Brushes.DarkRed);
                return;
            }

            if (currentAlias != null && !string.IsNullOrEmpty(AliasBox.Text))
            {
                if (MessageBox.Show($"Update the server value for ALL the connection strings in the file: '{currentAlias.Alias}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }
                ClearStatusMessage();
                updateServersByFile(currentAlias, true);
            }

            if (currentDevEnv != null && !string.IsNullOrEmpty(AliasBox.Text))
            {
                if (MessageBox.Show($"Update the server value for ALL the FILES in this ENVIRONMENT: '{currentDevEnv.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }

                ClearStatusMessage();
                foreach (var item in currentDevEnv.Files)
                {
                    updateServersByFile(item);
                }
            }
        }

        private async void updateServersByFile(AliasItem _alias, bool showStatus = false)
        {
            try
            {
                var filePath = GetEnvironmentName(_alias);

                if (!File.Exists(filePath))
                {
                    //StatusTextFileServers.Text = "File not found.";
                    ShowStatusAsync("File not found.", System.Windows.Media.Brushes.DarkRed);
                    return;
                }

                var text = File.ReadAllText(filePath);
                var newServer = FileServersCombo.Text?.Trim() ?? "";

                switch (_alias.Alias_Enum)
                {
                    case AliasEnum.SdmAppWebConfig:
                    case AliasEnum.SdmAppPubSubWebConfig:
                    case AliasEnum.STSWebConfig:
                        // <add name="X" connectionString="...">
                        text = UpdateElementAttribute(
                            text,
                            "add",
                            "connectionString",
                            attrs =>
                            {
                                if (!attrs.TryGetValue("name", out var name))
                                    return false;
                                return !ignoreKeyList(name);
                            },
                            old => UpdateConnectionString(old, newServer, "")
                        );

                        // <sessionState sqlConnectionString="...">
                        text = UpdateElementAttribute(
                            text,
                            "sessionState",
                            "sqlConnectionString",
                            attrs => true,
                            old => UpdateConnectionString(old, newServer, "")
                        );
                        break;

                    case AliasEnum.SdmAppBOMi2WebConfig:
                    case AliasEnum.SdmAppPubSubBOMi2WebConfig:
                        // <add key="DH2SQLServer" value="...">
                        text = UpdateElementAttribute(
                            text,
                            "add",
                            "value",
                            attrs =>
                                attrs.TryGetValue("key", out var key) &&
                                key.Equals("DH2SQLServer", StringComparison.OrdinalIgnoreCase),
                            old => UpdateConnectionString(old, newServer, "")
                        );
                        break;

                    case AliasEnum.SdmAppMonitoringConfiguration:
                        // <Setting name="ConnectionString" value="...">
                        text = UpdateElementAttribute(
                            text,
                            "Setting",
                            "value",
                            attrs =>
                                attrs.TryGetValue("name", out var name) &&
                                name.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase),
                            old => UpdateConnectionString(old, newServer, "")
                        );
                        break;

                    case AliasEnum.SdmLog4Net:
                        // <connectionString value="...">
                        text = UpdateElementAttribute(
                            text,
                            "connectionString",
                            "value",
                            attrs => true,
                            old => UpdateConnectionString(old, newServer, "")
                        );
                        break;
                }

                File.WriteAllText(filePath, text);

                //StatusTextFileServers.Text = "Saved!";
                ////await Task.Delay(3000);
                //StatusTextFileServers.Text = string.Empty;
                //if(showStatus)
                    ShowStatusAsync("Saved!", System.Windows.Media.Brushes.DarkRed);

                //LoadAliases();
                RefreshUI(_alias.EnvironmentName, false);
            }
            catch (Exception ex)
            {
                //StatusTextFileServers.Text = "Error: " + ex.Message;
                ShowStatusAsync(("Error: " + ex.Message), System.Windows.Media.Brushes.DarkRed);
            }
        }

        #endregion

        #region Databases listing & auth

        private ServerEntry FindServerEntry(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            return servers.FirstOrDefault(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        }

        private async Task PopulateDatabasesAsync(string serverAddress, string fileDb, CancellationToken token)
        {
            StatusMessagePanel.Visibility = Visibility.Collapsed; //status should always hide when a database is selected, until it resolves.

            ConnStringsPanel.Visibility = Visibility.Visible;
            DbLoadingPanel.Visibility = Visibility.Visible;
            DatabaseCombo.IsEnabled = false;
            await DoEventsAsync();

            var dbs = new List<string>();
            serverAddress = serverAddress.Trim('\'', ' ');
            fileDb = fileDb.Trim('\'', ' ');

            bool serverOk = false;

            // 1. Check cache first
            if (!string.IsNullOrWhiteSpace(serverAddress) &&
                _databaseCache.TryGetValue(serverAddress, out var cachedList))
            {
                if (cachedList != null)
                {
                    // Cached successful result
                    dbs = new List<string>(cachedList);
                    serverOk = true;
                }
                else
                { // Cached failure → skip SQL entirely
                    serverOk = false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(serverAddress))
            {
                // 2. No cache → try to load from SQL Server
                var entry = FindServerEntry(serverAddress);

                try
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = serverAddress,
                        InitialCatalog = "master",
                        ConnectTimeout = 3,
                        TrustServerCertificate = true,
                        IntegratedSecurity = true //entry == null
                    };

                    using var conn = new SqlConnection(builder.ConnectionString);
                    await conn.OpenAsync(token);

                    using var cmd = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", conn);
                    using var rdr = await cmd.ExecuteReaderAsync(token);

                    while (await rdr.ReadAsync(token))
                        dbs.Add(rdr.GetString(0));

                    // Cache successful result
                    _databaseCache[serverAddress] = new List<string>(dbs);
                }
                catch
                {
                    // Cache failure so we don't retry again
                    //_databaseCache[serverAddress] = null;

                    // if server isn't available don't cache
                    //await ShowStatusAsync("Server not accessible", 3);
                }
            }

            // ALWAYS insert fileDb
            if (!string.IsNullOrWhiteSpace(fileDb) &&
                !dbs.Any(d => d.Equals(fileDb, StringComparison.OrdinalIgnoreCase)))
            {
                //dbs.Insert(0, fileDb);
                DatabaseCombo.Text = fileDb; // user can still type over this DatabaseCombo.SelectedIndex = -1;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                DatabaseCombo.ItemsSource = dbs;

                if (!string.IsNullOrWhiteSpace(fileDb))
                {
                    DatabaseCombo.SelectedItem = dbs
                        .FirstOrDefault(x => x.Equals(fileDb, StringComparison.OrdinalIgnoreCase))
                        ?? fileDb;
                }
                else
                {
                    DatabaseCombo.SelectedIndex = 0;
                }

            });            

            DbLoadingPanel.Visibility = Visibility.Collapsed;
            DatabaseCombo.IsEnabled = true;

            //recalculate the height based on its items
            //DatabaseCombo.IsEditable = true;

            if (!_databaseCache.TryGetValue(serverAddress, out var serverAccessible))
            {
                if (serverAccessible == null)
                {
                    ShowStatusAsync("Server not accessible", System.Windows.Media.Brushes.DarkOrange);
                    //DatabaseCombo.IsEnabled = false;
                    //DatabaseCombo.IsEditable = false;
                }
            }
        }


        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressServerComboEvent)
                return;

            // Cancel any previous DB load
            _dbLoadCts?.Cancel();
            _dbLoadCts = new CancellationTokenSource();
            var token = _dbLoadCts.Token;

            var server = ServerCombo.SelectedItem as string ?? ServerCombo.Text;
            var currentDb = DatabaseCombo.Text;

            await PopulateDatabasesAsync(server, currentDb, token);
        }

        private void DatabaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //clearMessages();
        }

        #endregion

        private async void BtnReloadFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear current selections
                currentAlias = null;
                currentConn = (null, null);

                AliasPanel.Visibility = Visibility.Collapsed;
                ConnStringsPanel.Visibility = Visibility.Collapsed;

                // Clear UI panels
                //DatabaseCombo.ItemsSource = null;
                //ServerCombo.ItemsSource = null;

                // Reload everything
                LoadAliases();
                RefreshUI("", false);

                //StatusText.Text = "Reloading files...";
                //StatusTextFileServers.Text = "";

                //StatusText.Text = "Files reloaded.";
                ShowStatusAsync("Files Reloaded", System.Windows.Media.Brushes.DarkRed);
            }
            catch (Exception ex)
            {
                //StatusText.Text = "Error reloading files: " + ex.Message;
                ShowStatusAsync(("Error reloading files: " + ex.Message), System.Windows.Media.Brushes.DarkOrange);
            }
        }

        #region Save connection

        private async void BtnSaveConn_Click(object sender, RoutedEventArgs e)
        {
            if (currentConn.name == null || currentConn.element == null || currentAlias == null)
            {
                //StatusText.Text = "No connection selected.";
                ShowStatusAsync("No connection selected.", System.Windows.Media.Brushes.DarkOrange);
                return;
            }

            try
            {
                var filePath = GetEnvironmentName(currentAlias);
                if (!File.Exists(filePath))
                {
                    //StatusText.Text = "File not found.";
                    ShowStatusAsync("File now found", System.Windows.Media.Brushes.DarkOrange);
                    return;
                }

                ClearStatusMessage();
                var text = File.ReadAllText(filePath);
                var newServer = ServerCombo.Text?.Trim() ?? "";
                var newDb = DatabaseCombo.Text?.Trim() ?? "";

                switch (currentAlias.Alias_Enum)
                {
                    case AliasEnum.SdmAppWebConfig:
                    case AliasEnum.SdmAppPubSubWebConfig:
                    case AliasEnum.STSWebConfig:
                        // <add name="X" connectionString="...">
                        text = UpdateElementAttribute(
                            text,
                            "add",
                            "connectionString",
                            attrs =>
                                attrs.TryGetValue("name", out var name) &&
                                name.Equals(currentConn.name, StringComparison.OrdinalIgnoreCase),
                            old => UpdateConnectionString(old, newServer, newDb)
                        );

                        // <sessionState mode="X" sqlConnectionString="...">
                        text = UpdateElementAttribute(
                            text,
                            "sessionState",
                            "sqlConnectionString",
                            attrs =>
                                attrs.TryGetValue("mode", out var mode) &&
                                mode.Equals(currentConn.name, StringComparison.OrdinalIgnoreCase),
                            old => UpdateConnectionString(old, newServer, newDb)
                        );
                        break;

                    case AliasEnum.SdmAppBOMi2WebConfig:
                    case AliasEnum.SdmAppPubSubBOMi2WebConfig:
                        // <add key="DH2SQLServer" value="...">
                        text = UpdateElementAttribute(
                            text,
                            "add",
                            "value",
                            attrs =>
                                attrs.TryGetValue("key", out var key) &&
                                key.Equals(currentConn.name, StringComparison.OrdinalIgnoreCase),
                            old => UpdateConnectionString(old, newServer, newDb)
                        );
                        break;

                    case AliasEnum.SdmAppMonitoringConfiguration:
                        {
                            // Which ProfileConsumer does this connection belong to?
                            var consumerName = currentConn.element
                                .Parent?      // <Settings>
                                .Parent?      // <ProfileConsumer>
                                .Attribute("name")?.Value;

                            if (string.IsNullOrEmpty(consumerName))
                            {
                                //StatusText.Text = "Connection entry not found in file.";
                                ShowStatusAsync("Connection entry not found in file.", System.Windows.Media.Brushes.DarkOrange);
                                return;
                            }

                            text = UpdateProfileConsumerSetting(
                                text,
                                consumerName,
                                old => UpdateConnectionString(old, newServer, newDb)
                            );

                            break;
                        }

                    case AliasEnum.SdmLog4Net:
                        // <connectionString value="..."> inside <appender name="ErrorHandlerModule_SQLAppender">
                        text = UpdateElementAttribute(
                            text,
                            "connectionString",
                            "value",
                            attrs => true,
                            old => UpdateConnectionString(old, newServer, newDb)
                        );
                        break;
                }

                File.WriteAllText(filePath, text);

                //StatusText.Text = "Saved.";
                ShowStatusAsync("Saved.",System.Windows.Media.Brushes.DarkRed);
                //LoadAliases();
                RefreshUI(GetEnvironmentName(currentAlias), false);
            }
            catch (Exception ex)
            {
                //StatusText.Text = "Error: " + ex.Message;
                ShowStatusAsync(("Error: " + ex.Message), System.Windows.Media.Brushes.DarkOrange);
            }
        }

        private static string UpdateConnectionString(string original, string newServer, string newDb)
        {
            if (original == null) original = "";
            var parts = original.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

            bool serverSet = false, dbSet = false;

            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var key = p.Split('=')[0].Trim();
                var _value = p.Split('=')[1].Trim();
                if (key.Equals("Server", StringComparison.OrdinalIgnoreCase) || key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                {
                    if (_value.StartsWith("'"))
                    {
                        //Server/Data Source wrapped with single quotes
                        parts[i] = key + "='" + newServer + "'";
                    }
                    else
                    {
                        parts[i] = key + "=" + newServer;
                    }
                    serverSet = true;
                }
                else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                {
                    if (_value.StartsWith("'"))
                    {
                        //Database/Initial Catalog wrapped with single quotes
                        parts[i] = string.IsNullOrEmpty(newDb) ? parts[i] : (key + "='" + newDb + "'");
                    }
                    else
                    {
                        parts[i] = string.IsNullOrEmpty(newDb) ? parts[i] : (key + "=" + newDb);
                    }

                    dbSet = true;
                }
            }

            if (!serverSet && !string.IsNullOrEmpty(newServer))
                parts.Add("Server=" + newServer);
            if (!dbSet && !string.IsNullOrEmpty(newDb))
                parts.Add("Database=" + newDb);

            var s = string.Join(";", parts);
            //if (!s.EndsWith(";")) s += ";";
            return s;
        }

        #endregion

        #region Helpers

        private string GetServersFilePath()
        {
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            return Path.Combine(appFolder, "servers.json");
        }

        private static List<ServerEntry> GetDefaultServers()
        {
            return new List<ServerEntry>
            {
                new ServerEntry { Name = "Activation & Employment Support" , Address = "VSINS-Dev-SQ9\\DEV1" },
                new ServerEntry { Name = "Sligo Business Improvement" , Address = "VSINS-Dev-SQ9\\DEV2" },
                new ServerEntry { Name = "Web Self Service" , Address = "VSINS-Dev-SQ9\\DEV3" },
                new ServerEntry { Name = "Core Platform Team" , Address = "VSINS-Dev-SQ9\\DEV4" },
                new ServerEntry { Name = "LTSM" , Address = "VSINS-Dev-SQ9\\DEV5" },
                new ServerEntry { Name = "BOMi Modeling" , Address = "VSINS-Dev-SQ9\\DEV6" },
                new ServerEntry { Name = "Business Change Team" , Address = "VSINS-Dev-SQ9\\DEV7" },
                new ServerEntry { Name = "Household Benefit" , Address = "VSINS-Dev-SQ9\\DEV8" },
                new ServerEntry { Name = "Production Support" , Address = "VSINS-Dev-SQ9\\DEV9" },
                new ServerEntry { Name = "DEV10" , Address = "VSINS-Dev-SQ9\\DEV10" },
                new ServerEntry { Name = "FV1" , Address = "vsins-copy-sq01\\COPY1" },
                new ServerEntry { Name = "FV2" , Address = "vsins-full-sq01\\FULL2" },
                new ServerEntry { Name = "FV3" , Address = "vsins-full-sq01\\FULL3" },
                new ServerEntry { Name = "FV4" , Address = "vsins-full-sq01\\FULL4" },
                new ServerEntry { Name = "FV5" , Address = "vsins-full-sq02\\FULL5" },
                new ServerEntry { Name = "FV6" , Address = "vsins-full-sq02\\FULL6" },
                new ServerEntry { Name = "FV7" , Address = "vsins-full-sq02\\FULL7" },
                new ServerEntry { Name = "FV8" , Address = "vsins-full-sq02\\FULL8" },
                new ServerEntry { Name = "FV9" , Address = "vsins-full-sq03\\FULL9" },
                new ServerEntry { Name = "FV10" , Address = "vsins-full-sq03\\FULL10" },
                new ServerEntry { Name = "FV11" , Address = "vsins-full-sq03\\FULL11" },
                new ServerEntry { Name = "FV12" , Address = "vsins-full-sq03\\FULL12" },
                new ServerEntry { Name = "FV13" , Address = "vsins-full-sq04\\FULL13" },
                new ServerEntry { Name = "FV17" , Address = "vsins-full-sq04\\FULL14" },
                new ServerEntry { Name = "FV18" , Address = "vsins-full-sq04\\FULL15" },
                new ServerEntry { Name = "FV19" , Address = "vsins-full-sq04\\FULL16" },
                new ServerEntry { Name = "FV20" , Address = "cskns-bomi-sql1\\FV20" },
                new ServerEntry { Name = "FV30" , Address = "CSGNC-BOM-SQ102\\FV30" },
                new ServerEntry { Name = "FV50" , Address = "vsins-Sim-sq01\\Sim1" },
                new ServerEntry { Name = "PARTIAL1" , Address = "VSINS-DEV-SQ1\\partial1" },
                new ServerEntry { Name = "PARTIAL2" , Address = "VSINS-Dev-SQ1\\Partial2" },
                new ServerEntry { Name = "PARTIAL3" , Address = "VSINS-Dev-SQ1\\Partial3" },
                new ServerEntry { Name = "PARTIAL4" , Address = "VSINS-Dev-SQ1\\Partial4" },
                new ServerEntry { Name = "PARTIAL5" , Address = "VSINS-Dev-SQ1\\Partial5" },
                new ServerEntry { Name = "PARTIAL6" , Address = "VSINS-Dev-SQ1\\Partial6" },
                new ServerEntry { Name = "PARTIAL7" , Address = "VSINS-Dev-SQ1\\Partial7" },
                new ServerEntry { Name = "PARTIAL8" , Address = "VSINS-Dev-SQ1\\Partial8" },
                new ServerEntry { Name = "PARTIAL9" , Address = "VSINS-Dev-SQ1\\Partial9" },
                new ServerEntry { Name = "PARTIAL10" , Address = "VSINS-Dev-SQ1\\Partial10" },
                new ServerEntry { Name = "PARTIAL11" , Address = "VSINS-Dev-SQ2\\Partial11" },
                new ServerEntry { Name = "PARTIAL12" , Address = "VSINS-Dev-SQ2\\Partial12" },
                new ServerEntry { Name = "PARTIAL13" , Address = "VSINS-Dev-SQ2\\Partial13" },
                new ServerEntry { Name = "PARTIAL14" , Address = "VSINS-Dev-SQ2\\Partial14" },
                new ServerEntry { Name = "PARTIAL15" , Address = "VSINS-Dev-SQ2\\Partial15" },
                new ServerEntry { Name = "PARTIAL16" , Address = "VSINS-Dev-SQ2\\Partial16" },
                new ServerEntry { Name = "PARTIAL17" , Address = "VSINS-Dev-SQ2\\Partial17" },
                new ServerEntry { Name = "PARTIAL18" , Address = "VSINS-Dev-SQ2\\Partial18" },
                new ServerEntry { Name = "PARTIAL19" , Address = "VSINS-Dev-SQ2\\Partial19" },
                new ServerEntry { Name = "PARTIAL20" , Address = "VSINS-Dev-SQ2\\Partial20" },
                new ServerEntry { Name = "PARTIAL21" , Address = "VSINS-Dev-SQ3\\Partial21" },
                new ServerEntry { Name = "PARTIAL22" , Address = "VSINS-Dev-SQ3\\Partial22" },
                new ServerEntry { Name = "PARTIAL23" , Address = "VSINS-Dev-SQ3\\Partial23" },
                new ServerEntry { Name = "PARTIAL24" , Address = "VSINS-Dev-SQ3\\Partial24" },
                new ServerEntry { Name = "PARTIAL25" , Address = "VSINS-Dev-SQ3\\Partial25" },
                new ServerEntry { Name = "PARTIAL26" , Address = "VSINS-Dev-SQ3\\Partial26" },
                new ServerEntry { Name = "PARTIAL27" , Address = "VSINS-Dev-SQ3\\Partial27" },
                new ServerEntry { Name = "PARTIAL28" , Address = "VSINS-Dev-SQ3\\Partial28" },
                new ServerEntry { Name = "PARTIAL29" , Address = "VSINS-Dev-SQ3\\Partial29" },
                new ServerEntry { Name = "PARTIAL31" , Address = "VSINS-Dev-SQ4\\Partial31" },
                new ServerEntry { Name = "PARTIAL32" , Address = "VSINS-Dev-SQ4\\Partial32" },
                new ServerEntry { Name = "PARTIAL33" , Address = "VSINS-Dev-SQ4\\Partial33" },
                new ServerEntry { Name = "PARTIAL34" , Address = "VSINS-Dev-SQ4\\Partial34" },
                new ServerEntry { Name = "PARTIAL35" , Address = "VSINS-Dev-SQ4\\Partial35" },
                new ServerEntry { Name = "PARTIAL36" , Address = "VSINS-Dev-SQ4\\Partial36" },
                new ServerEntry { Name = "PARTIAL37" , Address = "VSINS-Dev-SQ4\\Partial37" },
                new ServerEntry { Name = "PARTIAL38" , Address = "VSINS-Dev-SQ4\\Partial38" },
                new ServerEntry { Name = "PARTIAL39" , Address = "VSINS-Dev-SQ4\\Partial39" },
                new ServerEntry { Name = "PARTIAL40" , Address = "VSINS-Dev-SQ4\\Partial40" },
                new ServerEntry { Name = "PARTIAL41" , Address = "VSINS-Dev-SQ5\\Partial41" },
                new ServerEntry { Name = "PARTIAL42" , Address = "VSINS-Dev-SQ5\\Partial42" },
                new ServerEntry { Name = "PARTIAL43" , Address = "VSINS-Dev-SQ5\\Partial43" },
                new ServerEntry { Name = "PARTIAL44" , Address = "VSINS-DEV-SQ5\\partial44" },
                new ServerEntry { Name = "PARTIAL45" , Address = "VSINS-Dev-SQ5\\Partial45" },
                new ServerEntry { Name = "PARTIAL48" , Address = "VSINS-Dev-SQ5\\Partial48" },
                new ServerEntry { Name = "PARTIAL49" , Address = "VSINS-Dev-SQ5\\Partial49" },
                new ServerEntry { Name = "PARTIAL50" , Address = "VSINS-Dev-SQ5\\Partial50" },
                new ServerEntry { Name = "PARTIAL51" , Address = "VSINS-Dev-SQ6\\Partial51" },
                new ServerEntry { Name = "PARTIAL52" , Address = "VSINS-Dev-SQ6\\Partial52" },
                new ServerEntry { Name = "PARTIAL53" , Address = "VSINS-Dev-SQ6\\Partial53" },
                new ServerEntry { Name = "PARTIAL54" , Address = "VSINS-Dev-SQ6\\Partial54" },
                new ServerEntry { Name = "PARTIAL55" , Address = "VSINS-Dev-SQ6\\Partial55" },
                new ServerEntry { Name = "PARTIAL56" , Address = "VSINS-Dev-SQ6\\Partial56" },
                new ServerEntry { Name = "PARTIAL57" , Address = "VSINS-Dev-SQ6\\Partial57" },
                new ServerEntry { Name = "PARTIAL58" , Address = "VSINS-Dev-SQ6\\Partial58" },
                new ServerEntry { Name = "PARTIAL59" , Address = "VSINS-Dev-SQ6\\Partial59" },
                new ServerEntry { Name = "PARTIAL60" , Address = "VSINS-Dev-SQ6\\Partial60" },
                new ServerEntry { Name = "PARTIAL61" , Address = "VSINS-Dev-SQ7\\Partial61" },
                new ServerEntry { Name = "PARTIAL62" , Address = "VSINS-Dev-SQ7\\Partial62" },
                new ServerEntry { Name = "PARTIAL63" , Address = "VSINS-Dev-SQ7\\Partial63" },
                new ServerEntry { Name = "PARTIAL64" , Address = "VSINS-Dev-SQ7\\Partial64" },
                new ServerEntry { Name = "Partial65" , Address = "VSINS-Dev-SQ7\\Partial65" },
                new ServerEntry { Name = "PARTIAL66" , Address = "VSINS-Dev-SQ7\\Partial66" },
                new ServerEntry { Name = "PARTIAL67" , Address = "VSINS-Dev-SQ7\\Partial67" },
                new ServerEntry { Name = "PARTIAL68" , Address = "VSINS-Dev-SQ7\\Partial68" },
                new ServerEntry { Name = "PARTIAL69" , Address = "VSINS-Dev-SQ7\\Partial69" },
                new ServerEntry { Name = "PARTIAL70" , Address = "VSINS-Dev-SQ7\\Partial70" },
            };
        }

        private DevEnvironment GetEnvironment(string name)
        {
            var devEnv = DevEnvs.FirstOrDefault(e => e.Name == name);
            if (devEnv == null)
                return new DevEnvironment();
            return devEnv;
        }

        private string GetEnvironmentName(AliasItem alias)
        {
            return alias.GetPath(GetEnvironment(alias.EnvironmentName));
        }

        private static string ParseConn(string connString, string[] keys)
        {
            if (string.IsNullOrWhiteSpace(connString)) return "";
            var parts = connString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var idx = p.IndexOf('=');
                if (idx < 0) continue;
                var k = p.Substring(0, idx).Trim();
                var v = p.Substring(idx + 1).Trim();
                foreach (var key in keys)
                    if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                        return v;
            }
            return "";
        }

        private static string UpdateElementAttribute(
            string xml,
            string elementName,
            string attributeName,
            Func<Dictionary<string, string>, bool> elementFilter,
            Func<string, string> updateFunc)
        {
            string pattern = $@"(<{elementName}\b[^>]*?\s{attributeName}\s*=\s*"")(.*?)("")";

            return Regex.Replace(
                xml,
                pattern,
                match =>
                {
                    string fullTag = match.Value;

                    // Extract all attributes from the element
                    var attributes = Regex.Matches(fullTag, @"(\w+)\s*=\s*""([^""]*)""")
                        .Cast<Match>()
                        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);

                    // Apply filter (e.g., ignoreKeyList(name))
                    if (!elementFilter(attributes))
                        return match.Value;

                    string oldValue = match.Groups[2].Value;
                    string newValue = updateFunc(oldValue);

                    return match.Groups[1].Value + newValue + match.Groups[3].Value;
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
        }

        private static string UpdateProfileConsumerSetting(
        string xml,
        string consumerName,
        Func<string, string> updateFunc)
        {
            // Capture exactly one <ProfileConsumer name="X"> ... </ProfileConsumer>
            var pattern =
                $@"(<ProfileConsumer\b[^>]*\bname\s*=\s*""{Regex.Escape(consumerName)}""[^>]*>)(.*?)(</ProfileConsumer>)";

            return Regex.Replace(
                xml,
                pattern,
                match =>
                {
                    var startTag = match.Groups[1].Value;
                    var inner = match.Groups[2].Value;
                    var endTag = match.Groups[3].Value;

                    // Inside this consumer, update <Setting name="ConnectionString" value="...">
                    inner = UpdateElementAttribute(
                        inner,
                        "Setting",
                        "value",
                        attrs =>
                            attrs.TryGetValue("name", out var nameAttr) &&
                            nameAttr.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase),
                        updateFunc
                    );

                    return startTag + inner + endTag;
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
        }

        //public async Task ShowStatusAsync(string message, System.Windows.Media.Brush foregroundColor) //, int seconds = 3)
        public void ShowStatusAsync(string message, System.Windows.Media.Brush foregroundColor) //, int seconds = 3)
        {

            //StatusMessagePanel.Visibility = Visibility.Visible;
            //StatusMessage.Text = message;
            //StatusMessage.Foreground = foregroundColor;
            //await Task.Delay(seconds * 1000);
            //StatusMessagePanel.Visibility = Visibility.Collapsed;
            //textBlock.Text = "";
            StatusMessagePanel.Visibility = Visibility.Visible;
            StatusMessage.Text = message;
            StatusMessage.Foreground = foregroundColor;
            //await Task.Delay(seconds * 1000);
            //_statusTimer.Interval = TimeSpan.FromSeconds(seconds); _statusTimer.Stop(); // reset if already running _statusTimer.Start();
            //panel.Visibility = Visibility.Collapsed;
        }

        public void ClearStatusMessage()
        {
            StatusMessagePanel.Visibility = Visibility.Collapsed;
            StatusMessage.Text = string.Empty;
        }

        #endregion

    }
}
