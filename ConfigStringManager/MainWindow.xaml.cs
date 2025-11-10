using Microsoft.Data.SqlClient;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ConfigStringManager
{
    public partial class MainWindow : Window
    {
        private readonly string appFolder;
        private readonly string aliasesPath;
        private readonly string serversPath;

        private List<AliasItem> aliases = new();
        private List<ServerEntry> servers = new();

        private AliasItem currentAlias = null;
        private (string name, XElement element) currentConn = (null, null);

        public MainWindow()
        {
            InitializeComponent();

            appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConfigStringManager");
            Directory.CreateDirectory(appFolder);
            aliasesPath = Path.Combine(appFolder, "config_files.json");
            serversPath = Path.Combine(appFolder, "servers.json");

            LoadServers();
            LoadAliases();
            RefreshUI();
        }

        #region Models
        private class AliasItem
        {
            public string Alias { get; set; }
            public string Path { get; set; }
            public override string ToString() => $"{Alias} ({System.IO.Path.GetFileName(Path)})";
        }

        private class ServerEntry
        {
            public string Name { get; set; }
            public string Address { get; set; }
            public bool UseSqlAuth { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Display => $"{Name} — {Address}" + (UseSqlAuth ? " (SQL Auth)" : " (Integrated)");
        }
        #endregion

        #region Load/Save

        private void LoadServers()
        {
            try
            {
                if (File.Exists(serversPath))
                {
                    var json = File.ReadAllText(serversPath);
                    servers = JsonSerializer.Deserialize<List<ServerEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ServerEntry>();
                }
                else
                {
                    servers = new List<ServerEntry>();
                    SaveServers();
                }
            }
            catch
            {
                servers = new List<ServerEntry>();
            }
            RenderServerList();
        }

        private void SaveServers()
        {
            try
            {
                File.WriteAllText(serversPath, JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save servers.json: " + ex.Message);
            }
            RenderServerList();
        }

        private void LoadAliases()
        {
            try
            {
                if (File.Exists(aliasesPath))
                {
                    var json = File.ReadAllText(aliasesPath);
                    aliases = JsonSerializer.Deserialize<List<AliasItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<AliasItem>();
                }
                else
                {
                    aliases = new List<AliasItem>();
                    SaveAliases();
                }
            }
            catch
            {
                aliases = new List<AliasItem>();
            }
        }

        private void SaveAliases()
        {
            try
            {
                File.WriteAllText(aliasesPath, JsonSerializer.Serialize(aliases, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed saving config list: " + ex.Message);
            }
            RefreshUI();
        }

        #endregion

        #region UI Rendering

        private void RefreshUI()
        {
            FilesTree.Items.Clear();

            foreach (var a in aliases)
            {
                var root = new TreeViewItem { Header = $"{a.Alias}  ({System.IO.Path.GetFileName(a.Path)})", Tag = a };
                root.Expanded += Root_Expanded;
                root.Selected += Root_Selected;
                root.Items.Add(new TreeViewItem { Header = "(expand to load)" });
                FilesTree.Items.Add(root);
                AliasPanel.Visibility = Visibility.Collapsed;
                ConnStringsPanel.Visibility = Visibility.Collapsed;
            }

            RenderServerList();
            ClearSelection();
        }

        private void RenderServerList()
        {
            ServersListBox.ItemsSource = null;
            ServersListBox.ItemsSource = servers.Select(s => new { Display = s.Display, Entry = s }).ToList();
        }

        private void ClearSelection()
        {
            currentAlias = null;
            currentConn = (null, null);
            AliasBox.Text = "";
            FilePathText.Text = "";
            ConnNameText.Text = "";
            ServerCombo.ItemsSource = null;
            DatabaseCombo.ItemsSource = null;
            StatusText.Text = "";
            ServerTestResult.Text = "";
            PanelConnEnabled(false);
        }

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
            if (root.Tag is not AliasItem alias) return;

            root.Items.Clear();

            if (!File.Exists(alias.Path))
            {
                root.Items.Add(new TreeViewItem { Header = "(file not found)" });
                return;
            }

            try
            {
                var xml = XDocument.Load(alias.Path);
                var adds = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase)) &&
                                                        x.Parent != null && string.Equals(x.Parent.Name.LocalName, "connectionStrings", StringComparison.OrdinalIgnoreCase))
                                             .ToList();

                if (!adds.Any())
                {
                    root.Items.Add(new TreeViewItem { Header = "(no connection strings found)" });
                }
                else
                {
                    foreach (var add in adds)
                    {
                        var name = add.Attribute("name")?.Value ?? "(unnamed)";
                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, add) };
                        child.Selected += Conn_Selected;
                        root.Items.Add(child);
                    }
                }
            }
            catch (Exception ex)
            {
                root.Items.Add(new TreeViewItem { Header = $"(error reading file: {ex.Message})" });
            }
        }

        private void Root_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem root) return;
            if (root.Tag is not AliasItem alias) return;

            currentAlias = alias;
            AliasBox.Text = alias.Alias;
            FilePathText.Text = alias.Path;
            ConnNameText.Text = string.Empty;
            PanelConnEnabled(false);
            StatusText.Text = string.Empty;
            StatusTextFileServers.Text = string.Empty;
            AliasPanel.Visibility = Visibility.Visible;
            ConnStringsPanel.Visibility = Visibility.Collapsed;
            PopulateFileServersCombo();
        }

        private void Conn_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem item) return;
            if (item.Tag is not Tuple<AliasItem, XElement> tuple) return;

            ServerCombo.Text = string.Empty;
            DatabaseCombo.Text = string.Empty;
            StatusTextFileServers.Text = string.Empty;

            currentAlias = tuple.Item1;
            var element = tuple.Item2;
            var name = element.Attribute("name")?.Value ?? "(unnamed)";
            currentConn = (name, element);

            AliasBox.Text = currentAlias.Alias;
            FilePathText.Text = currentAlias.Path;
            ConnNameText.Text = name;

            var raw = element.Attribute("connectionString")?.Value ?? "";
            var parsedServer = ParseConn(raw, new[] { "Server", "Data Source", "Address", "Addr" });
            var parsedDb = ParseConn(raw, new[] { "Database", "Initial Catalog" });

            PopulateServerCombo(parsedServer);
            PopulateDatabasesAsync(parsedServer, parsedDb);

            PanelConnEnabled(true);
            StatusText.Text = "";

            AliasPanel.Visibility = Visibility.Collapsed;
            ConnStringsPanel.Visibility = Visibility.Visible;
        }

        #endregion

        #region Server management UI

        private async void BtnCopyFilePath_Click(object sender, RoutedEventArgs e) 
        {
            System.Windows.Clipboard.SetText(FilePathText.Text);
            CopiedTextStatus.Visibility = Visibility.Visible;
            await Task.Delay(1000);
            CopiedTextStatus.Visibility = Visibility.Collapsed;
        }

        private void BtnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var name = ServerNameBox.Text?.Trim();
            var addr = ServerAddressBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(addr)) { MessageBox.Show("Address required"); return; }
            if (servers.Any(s => string.Equals(s.Address, addr, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Server address already exists.");
                return;
            }
            servers.Add(new ServerEntry
            {
                Name = string.IsNullOrWhiteSpace(name) ? addr : name,
                Address = addr,
                UseSqlAuth = UseSqlAuthCheck.IsChecked == true,
                Username = SqlUserBox.Text ?? "",
                Password = SqlPassBox.Password ?? ""
            });
            SaveServers();
            ServerNameBox.Text = ServerAddressBox.Text = "";
            SqlUserBox.Text = SqlPassBox.Password = "";
            UseSqlAuthCheck.IsChecked = false;
        }

        private void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListBox.SelectedItem == null) return;
            dynamic sel = ServersListBox.SelectedItem;
            var entry = sel.Entry as ServerEntry;
            if (entry == null) return;
            if (MessageBox.Show($"Remove server '{entry.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                servers.RemoveAll(s => string.Equals(s.Address, entry.Address, StringComparison.OrdinalIgnoreCase));
                SaveServers();
            }
        }

        private void ServersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServersListBox.SelectedItem == null) return;
            dynamic sel = ServersListBox.SelectedItem;
            var entry = sel.Entry as ServerEntry;
            if (entry == null) return;

            ServerNameBox.Text = entry.Name;
            ServerAddressBox.Text = entry.Address;
            UseSqlAuthCheck.IsChecked = entry.UseSqlAuth;
            SqlUserBox.Text = entry.Username;
            SqlPassBox.Password = entry.Password;
        }

        private void RenderServerComboItems(string fileServer)
        {
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

        private void PopulateServerCombo(string fileServer)
        {
            Dispatcher.Invoke(() => RenderServerComboItems(fileServer));
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
            if (currentAlias == null || AliasBox.Text.Equals(string.Empty))
            {
                StatusTextFileServers.Text = "No file/alias selected.";
                return;
            }

            if (MessageBox.Show($"Update the server value for ALL the connection strings in the file: '{currentAlias.Alias}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No)
            {
                return;
            }

            try
            {
                var filePath = currentAlias.Path;
                if (!File.Exists(filePath))
                {
                    StatusTextFileServers.Text = "File not found.";
                    return;
                }

                var xml = XDocument.Load(filePath);
                var conns = xml.Descendants().Where(x => 
                    string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                    (x.Attribute("connectionString") != null)).Select(x=>x);

                foreach (var item in conns)
                {
                    var old = (string)item.Attribute("connectionString") ?? "";
                    var newServer = FileServersCombo.Text?.Trim() ?? "";
                    var updated = UpdateConnectionString(old, newServer, string.Empty, true);
                    item.SetAttributeValue("connectionString", updated);
                }

                xml.Save(filePath);
                StatusTextFileServers.Text = "Saved!";
                await Task.Delay(3000);
                StatusTextFileServers.Text = string.Empty;
                LoadAliases();
                RefreshUI();
            }
            catch (Exception ex)
            {
                StatusTextFileServers.Text = "Error: " + ex.Message;
            }
        }

        #endregion

        #region Databases listing & auth

        private ServerEntry FindServerEntry(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            return servers.FirstOrDefault(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        }

        private async void PopulateDatabasesAsync(string serverAddress, string fileDb)
        {
            DatabaseCombo.ItemsSource = null;
            var dbs = new List<string>();

            if (!string.IsNullOrWhiteSpace(serverAddress))
            {
                var entry = FindServerEntry(serverAddress);
                try
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = serverAddress,
                        InitialCatalog = "master",
                        ConnectTimeout = 3,
                        TrustServerCertificate = true
                    };

                    if (entry != null && entry.UseSqlAuth)
                    {
                        builder.UserID = entry.Username;
                        builder.Password = entry.Password;
                        builder.IntegratedSecurity = false;
                    }
                    else
                    {
                        builder.IntegratedSecurity = true;
                    }

                    using var conn = new SqlConnection(builder.ConnectionString);
                    await conn.OpenAsync();

                    using var cmd = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", conn);
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        dbs.Add(rdr.GetString(0));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DB list load failed: " + ex.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(fileDb) && !dbs.Any(d => string.Equals(d, fileDb, StringComparison.OrdinalIgnoreCase)))
                dbs.Insert(0, fileDb);

            Dispatcher.Invoke(() =>
            {
                DatabaseCombo.ItemsSource = dbs;
                if (!string.IsNullOrWhiteSpace(fileDb) && dbs.Contains(fileDb)) DatabaseCombo.SelectedItem = fileDb;
                else if (dbs.Count > 0) DatabaseCombo.SelectedIndex = 0;
            });
        }

        private void BtnLoadDbs_Click(object sender, RoutedEventArgs e)
        {
            var server = ServerCombo.Text;
            var curDb = DatabaseCombo.Text;
            PopulateDatabasesAsync(server, curDb);
        }

        private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var server = ServerCombo.SelectedItem as string ?? ServerCombo.Text;
            var currentDb = DatabaseCombo.Text;
            PopulateDatabasesAsync(server, currentDb);
        }

        #endregion

        #region Test connection / Import Export servers

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            ServerTestResult.Text = "";
            if (ServersListBox.SelectedItem == null) { ServerTestResult.Text = "Select a server on the right first."; return; }
            dynamic sel = ServersListBox.SelectedItem;
            var entry = sel.Entry as ServerEntry;
            if (entry == null) { ServerTestResult.Text = "Invalid selection."; return; }

            ServerTestResult.Text = "Testing...";
            var ok = await TestSqlConnectionAsync(entry);
            ServerTestResult.Text = ok ? "✅ Connection successful." : "❌ Connection failed (see console).";
            ServerTestResult.Foreground = ok ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        private Task<bool> TestSqlConnectionAsync(ServerEntry entry)
        {
            return Task.Run(() =>
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = entry.Address,
                        InitialCatalog = "master",
                        ConnectTimeout = 3,
                        TrustServerCertificate = true
                    };

                    if (entry.UseSqlAuth)
                    {
                        builder.UserID = entry.Username;
                        builder.Password = entry.Password;
                        builder.IntegratedSecurity = false;
                    }
                    else
                    {
                        builder.IntegratedSecurity = true;
                    }

                    using var conn = new SqlConnection(builder.ConnectionString);
                    conn.Open();
                    conn.Close();
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("TestConn failed: " + ex.Message);
                    return false;
                }
            });
        }

        #endregion

        #region Save connection

        private void BtnSaveConn_Click(object sender, RoutedEventArgs e)
        {
            if (currentConn.name == null || currentConn.element == null || currentAlias == null)
            {
                StatusText.Text = "No connection selected.";
                return;
            }

            try
            {
                var filePath = currentAlias.Path;
                if (!File.Exists(filePath))
                {
                    StatusText.Text = "File not found.";
                    return;
                }

                var xml = XDocument.Load(filePath);
                var add = xml.Descendants().FirstOrDefault(x =>
                    string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string)x.Attribute("name") ?? "", currentConn.name, StringComparison.OrdinalIgnoreCase) &&
                    (x.Attribute("connectionString") != null));

                if (add == null)
                {
                    StatusText.Text = "Connection entry not found in file.";
                    return;
                }

                var old = (string)add.Attribute("connectionString") ?? "";
                var newServer = ServerCombo.Text?.Trim() ?? "";
                var newDb = DatabaseCombo.Text?.Trim() ?? "";

                var updated = UpdateConnectionString(old, newServer, newDb, false);
                add.SetAttributeValue("connectionString", updated);

                xml.Save(filePath);

                StatusText.Text = "Saved.";
                LoadAliases();
                RefreshUI();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
        }

        private static string UpdateConnectionString(string original, string newServer, string newDb, bool onlyServer)
        {
            if (original == null) original = "";
            var parts = original.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

            bool serverSet = false, dbSet = false;

            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var key = p.Split('=')[0].Trim();
                if (key.Equals("Server", StringComparison.OrdinalIgnoreCase) || key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "Server=" + newServer;
                    serverSet = true;
                }
                else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase) && !onlyServer)
                {
                    parts[i] = "Database=" + newDb;
                    dbSet = true;
                }
            }

            if (!serverSet && !string.IsNullOrEmpty(newServer))
                parts.Add("Server=" + newServer);
            if (!dbSet && !string.IsNullOrEmpty(newDb) && !onlyServer)
                parts.Add("Database=" + newDb);

            var s = string.Join(";", parts);
            if (!s.EndsWith(";")) s += ";";
            return s;
        }

        #endregion

        #region Alias management

        private void BtnAddFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Config files (*.config;*.xml)|*.config;*.xml|All files|*.*", Multiselect = true };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    if (!aliases.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase)))
                        aliases.Add(new AliasItem { Alias = System.IO.Path.GetFileNameWithoutExtension(f), Path = f });
                }
                SaveAliases();
            }
        }

        private void BtnSaveAlias_Click(object sender, RoutedEventArgs e)
        {
            if (currentAlias == null)
            {
                StatusText.Text = "Select an alias first.";
                return;
            }
            currentAlias.Alias = AliasBox.Text?.Trim() ?? currentAlias.Alias;
            SaveAliases();
            StatusText.Text = "Alias saved.";
        }

        private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (currentAlias == null) return;
            if (MessageBox.Show($"Remove alias '{currentAlias.Alias}' from list?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                aliases.RemoveAll(x => string.Equals(x.Path, currentAlias.Path, StringComparison.OrdinalIgnoreCase));
                SaveAliases();
            }
        }

        #endregion

        #region Helpers

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

        #endregion

        private void AliasBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void ServerNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
