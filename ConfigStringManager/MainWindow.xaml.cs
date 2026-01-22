using Enums.Services;
using Microsoft.Data.SqlClient;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;
using MessageBox = System.Windows.MessageBox;

namespace ConfigStringManager
{
    public partial class MainWindow : Window
    {
        private readonly string appFolder;
        private readonly string aliasesPath;
        private readonly string serversPath;
        private readonly string ignoreListPath;

        private List<DevEnvironment> devEnvs = new();
        private List<ServerEntry> servers = new();
        private List<IgnoreListEntry> ignoreListEntries = new();

        private DevEnvironment currentDevEnv = null;
        private AliasItem currentAlias = null;
        private (string name, XElement element) currentConn = (null, null);

        public MainWindow()
        {
            InitializeComponent();

            appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ConfigStringManager");
            Directory.CreateDirectory(appFolder);
            aliasesPath = Path.Combine(appFolder, "config_files.json");
            serversPath = Path.Combine(appFolder, "servers.json");
            ignoreListPath = Path.Combine(appFolder, "ignoreList.json");

            LoadServers();
            LoadAliases();
            RefreshUI();
        }

        #region Models

        private enum AliasEnum
        {
            [Description("SdmApp.WebConfig")]
            SdmAppWebConfig = 1,
            [Description("SdmApp.Web-bomi2AppSettings.config")]
            SdmAppBOMi2WebConfig = 2,
            [Description("SdmApp.PubSub.WebConfig")]
            SdmAppPubSubWebConfig = 3,
            [Description("SdmApp.PubSub.Web-bomi2AppSettings.config")]
            SdmAppPubSubBOMi2WebConfig = 4,
            [Description("SdmApp.MonitoringConfiguration")]
            SdmAppMonitoringConfiguration = 5,
            [Description("Sdm.Log4Net")]
            SdmLog4Net = 6,
            [Description("STS.WebConfig")]
            STSWebConfig = 7
        }

        private class DevEnvironment
        {
            public string Name { get; set; }
            public string PrefixPath { get; set; }
            public string STSPrefixPath { get; set; }
            public IList<AliasItem> Files { get { return getEnvironmentFiles(this); } }
            private IList<AliasItem> getEnvironmentFiles(DevEnvironment _env)
            {
                return new List<AliasItem>
            {
                new AliasItem(){ Alias_Enum = AliasEnum.SdmAppWebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Web.config", Environment = _env },
                new AliasItem(){ Alias_Enum = AliasEnum.SdmAppBOMi2WebConfig, SufixPath = "Code\\DELIVERY\\server\\asmx\\Web-bomi2AppSettings.config", Environment = _env },
                new AliasItem(){ Alias_Enum = AliasEnum.SdmAppPubSubWebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App.PubSub\\Web.config", Environment = _env },
                new AliasItem(){ Alias_Enum = AliasEnum.SdmAppPubSubBOMi2WebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App.PubSub\\bomi2\\server\\asmx\\Web-bomi2AppSettings.config", Environment = _env },
                new AliasItem(){ Alias_Enum = AliasEnum.SdmAppMonitoringConfiguration, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Config\\MonitoringConfiguration.xml", Environment = _env },
                new AliasItem(){ Alias_Enum = AliasEnum.SdmLog4Net, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Config\\Sdm.Log4Net.xml", Environment = _env },
                new AliasItem(){ IsSTS = true,  Alias_Enum = AliasEnum.STSWebConfig, SufixPath = "Sdm.App.STS\\Web.config", Environment = _env }
            };
            }
        }

        private class AliasItem
        {
            public AliasEnum Alias_Enum { get; set; }
            public string Alias { get { return Alias_Enum.GetDescription(); } }
            public string SufixPath { get; set; }
            public DevEnvironment Environment { get; set; }
            public bool IsSTS { get; set; }
            public override string ToString() => $"{Alias} - ################################";
            public string GetPath()
            {
                if (IsSTS)
                    return Path.Combine(Environment.STSPrefixPath, SufixPath);
                return Path.Combine(Environment.PrefixPath, SufixPath);
            }
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

        private class IgnoreListEntry
        {
            public string KeyEntry { get; set; }
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
            //RenderServerList();
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
        }

        private void LoadAliases()
        {
            try
            {
                if (File.Exists(aliasesPath))
                {
                    var json = File.ReadAllText(aliasesPath);
                    devEnvs = JsonSerializer.Deserialize<List<DevEnvironment>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<DevEnvironment>();
                }
                else
                {
                    devEnvs = new List<DevEnvironment>();
                    SaveAliases();
                }
            }
            catch
            {
                devEnvs = new List<DevEnvironment>();
            }
        }

        private void SaveAliases()
        {
            try
            {
                File.WriteAllText(aliasesPath, JsonSerializer.Serialize(devEnvs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed saving config list: " + ex.Message);
            }
            RefreshUI();
        }

        #endregion

        #region UI Rendering

        private void RefreshUI(DevEnvironment? envVisible = null, bool clearStatusText = true)
        {
            FilesTree.Items.Clear();

            foreach (var a in devEnvs)
            {
                var env = new TreeViewItem { Header = a.Name, Tag = a };
                env.Selected += Env_Selected;
                if (envVisible != null && envVisible.Name == a.Name)
                {
                    env.IsExpanded = true;
                }

                FilesTree.Items.Add(env);

                foreach (var f in a.Files)
                {
                    var root = new TreeViewItem { Header = $"{f.Alias}", Tag = f };
                    root.Expanded += Root_Expanded;
                    root.Selected += Root_Selected;
                    root.Items.Add(new TreeViewItem { Header = "(expand to load)" });
                    if (currentAlias != null && string.Equals(f.Alias, currentAlias.Alias, StringComparison.OrdinalIgnoreCase))
                    {
                        root.IsExpanded = true;
                    }
                    env.Items.Add(root);
                }
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
            clearMessages();
            PanelConnEnabled(false);
        }

        private void clearMessages()
        {
            //AliasBox.Text = "";
            //FilePathText.Text = "";
            //ConnNameText.Text = "";
            StatusText.Text = "";
            //ServerTestResult.Text = "";
            StatusTextFileServers.Text = "";
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

            var filePath = alias.GetPath();

            if (!File.Exists(filePath))
            {
                root.Items.Add(new TreeViewItem { Header = "(file not found)" });
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
                    root.Items.Add(new TreeViewItem { Header = "(no connection strings found)" });
                }
                else
                {
                    //WEBCONFIG & PUBSUB WEBCONFIG & STS
                    foreach (var add in adds)
                    {
                        var name = add.Attribute("name")?.Value ?? "(unnamed)";

                        if (ignoreKeyList(name))
                            continue;

                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, add) };
                        child.Selected += Conn_Selected;
                        root.Items.Add(child);
                    }

                    //SESSION STATE (DBSECURITY)
                    foreach (var add in addsCustom)
                    {
                        var name = add.Attribute("mode")?.Value ?? "(unnamed)";
                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, add), Foreground = System.Windows.Media.Brushes.DarkGreen, FontWeight = FontWeights.Bold };
                        child.Selected += Conn_Selected;
                        root.Items.Add(child);
                    }

                    //ASMX/BOMI2 & PUBSUB/ASMX/BOMI2
                    foreach (var add in addsCustom2)
                    {
                        var name = add.Attribute("key")?.Value ?? "(unnamed)";
                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, add), Foreground = System.Windows.Media.Brushes.BlueViolet, FontWeight = FontWeights.Bold };
                        child.Selected += Conn_Selected;
                        root.Items.Add(child);
                    }

                    //PERFORMANCE - PROFILING (MonitoringConfiguration.xml)
                    foreach (var add in addsCustom3)
                    {
                        var name = add.Attribute("name")?.Value ?? "(unnamed)";
                        var descendant = add.Descendants().Where(x => string.Equals(x.Name.LocalName, "Setting", StringComparison.OrdinalIgnoreCase) &&
                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) && a.Value.Equals("ConnectionString"))).FirstOrDefault();

                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, descendant), Foreground = System.Windows.Media.Brushes.DarkBlue, FontWeight = FontWeights.Bold };
                        child.Selected += Conn_Selected;
                        root.Items.Add(child);
                    }

                    //LOG4NET
                    foreach (var add in addsCustom4)
                    {
                        var name = add.Attribute("name")?.Value ?? "(unnamed)";
                        var descendant = add.Descendants().Where(x => string.Equals(x.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase) &&
                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase))).FirstOrDefault();//.Where(t=>t.Attribute("name")?.Value);
                        var child = new TreeViewItem { Header = name, Tag = new Tuple<AliasItem, XElement>(alias, descendant) }; //, Foreground = System.Windows.Media.Brushes.DarkOrange, FontWeight = FontWeights.Bold };
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

        private bool ignoreKeyList(string _key)
        {
            if (File.Exists(ignoreListPath))
            {
                //FILE EXISTS, READ THE IGNORE LIST
                var json = File.ReadAllText(ignoreListPath);
                ignoreListEntries = JsonSerializer.Deserialize<List<IgnoreListEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<IgnoreListEntry>();
            }
            else
            {
                //FILE DOESN'T EXIST
                //CREATES A FILE WITH THE DEFAULT ITEM
                ignoreListEntries = [
                    new IgnoreListEntry() { KeyEntry = "DH2CRSCodesServer" },  //from STS.Web.config
                new IgnoreListEntry() { KeyEntry = "PerformanceEntities" } //from Sdm.App.Web.config
                    ];
                try
                {
                    File.WriteAllText(ignoreListPath, JsonSerializer.Serialize(ignoreListEntries, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save ignoreList.json: " + ex.Message);
                }
            }

            return (ignoreListEntries.Any(s => string.Equals(s.KeyEntry, _key, StringComparison.OrdinalIgnoreCase)));
        }

        private void Env_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem env) return;

            if (env.Tag is not DevEnvironment devEnv) return;
            env.IsExpanded = true;
            currentAlias = null;
            currentDevEnv = devEnv;
            AliasBox.Text = devEnv.Name;
            FilePathText.Visibility = Visibility.Collapsed;
            btnCopyText.Visibility = Visibility.Collapsed;
            lblFile.Visibility = Visibility.Collapsed;
            ConnNameText.Text = string.Empty;
            //ServerTestResult.Text = string.Empty;
            PanelConnEnabled(false);
            //StatusText.Text = string.Empty;
            //StatusTextFileServers.Text = string.Empty;
            AliasPanel.Visibility = Visibility.Visible;
            ConnStringsPanel.Visibility = Visibility.Collapsed;
            PopulateFileServersCombo();
            clearMessages();
        }

        private void Root_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem root) return;

            if (root.Tag is not AliasItem alias) return;

            currentDevEnv = null;
            currentAlias = alias;
            AliasBox.Text = alias.Alias;
            FilePathText.Visibility = Visibility.Visible;
            btnCopyText.Visibility = Visibility.Visible;
            lblFile.Visibility = Visibility.Visible;
            FilePathText.Text = alias.GetPath();
            ConnNameText.Text = string.Empty;
            //ServerTestResult.Text = string.Empty;
            PanelConnEnabled(false);
            //StatusText.Text = string.Empty;
            //StatusTextFileServers.Text = string.Empty;
            AliasPanel.Visibility = Visibility.Visible;
            ConnStringsPanel.Visibility = Visibility.Collapsed;
            PopulateFileServersCombo();
            clearMessages();
        }

        private void Conn_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not TreeViewItem item) return;
            if (item.Tag is not Tuple<AliasItem, XElement> tuple) return;

            //ServerCombo.Text = string.Empty;
            //DatabaseCombo.Text = string.Empty;
            StatusTextFileServers.Text = string.Empty;
            DatabaseCombo.ItemsSource = null;
            //ServerTestResult.Text = string.Empty;

            currentAlias = tuple.Item1;
            var element = tuple.Item2;
            var name = string.Empty;
            var raw = string.Empty;

            switch (currentAlias.Alias_Enum)
            {

                case AliasEnum.SdmAppWebConfig:
                case AliasEnum.SdmAppPubSubWebConfig:
                case AliasEnum.SdmAppMonitoringConfiguration:
                case AliasEnum.STSWebConfig:
                    {
                        //name
                        if (element.Attribute("name") != null)
                        {
                            name = element.Attribute("name").Value;
                        }
                        else if (element.Attribute("mode") != null)
                        {
                            name = element.Attribute("mode").Value;
                        }

                        //raw
                        if (element.Attribute("connectionString") != null)
                        {
                            raw = element.Attribute("connectionString").Value;
                        }
                        else if (element.Attribute("sqlConnectionString") != null)
                        {
                            raw = element.Attribute("sqlConnectionString").Value;
                        }
                        else if (element.Attribute("value") != null)
                        {
                            raw = element.Attribute("value").Value;
                        }

                        break;
                    }
                case AliasEnum.SdmAppBOMi2WebConfig:
                case AliasEnum.SdmAppPubSubBOMi2WebConfig:
                    {
                        //name
                        if (element.Attribute("key") != null)
                        {
                            name = element.Attribute("key").Value;
                        }

                        //raw
                        if (element.Attribute("value") != null)
                        {
                            raw = element.Attribute("value").Value;
                        }

                        break;
                    }
                case AliasEnum.SdmLog4Net:
                    {
                        var isLog4NetFile = element.Parent?.Attributes().Any(a => a.Name.LocalName.Equals("name") && a.Value.Equals("ErrorHandlerModule_SQLAppender"));

                        //name
                        if (isLog4NetFile != null && isLog4NetFile == true) //find a better and generic solution
                        {
                            name = "ErrorHandlerModule_SQLAppender";
                        }

                        //raw
                        if (element.Attribute("value") != null)
                        {
                            raw = element.Attribute("value").Value;
                        }

                        break;
                    }
            }

            name = name.Trim();
            currentConn = (name, element);
            AliasBox.Text = currentAlias.Alias;
            FilePathText.Text = currentAlias.GetPath();
            ConnNameText.Text = name;

            var parsedServer = ParseConn(raw, new[] { "Server", "Data Source", "Address", "Addr" });
            var parsedDb = ParseConn(raw, new[] { "Database", "Initial Catalog" });
            PopulateServerCombo(parsedServer);
            PopulateDatabasesAsync(parsedServer, parsedDb);
            PanelConnEnabled(true);
            StatusText.Text = "";
            AliasPanel.Visibility = Visibility.Collapsed;
            ConnStringsPanel.Visibility = Visibility.Visible;
            clearMessages();
        }

        #endregion

        #region Server management UI

        private async void BtnCopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(FilePathText.Text);
            StatusTextFileServers.Text = "Copied!";
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
            if ((currentAlias == null || AliasBox.Text.Equals(string.Empty)) && (currentDevEnv == null || AliasBox.Text.Equals(string.Empty)))
            {
                StatusTextFileServers.Text = "No file/alias selected.";
                return;
            }

            if (currentAlias != null && !string.IsNullOrEmpty(AliasBox.Text))
            {
                if (MessageBox.Show($"Update the server value for ALL the connection strings in the file: '{currentAlias.Alias}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }
                updateServersByFile(currentAlias);
            }

            if (currentDevEnv != null && !string.IsNullOrEmpty(AliasBox.Text))
            {
                if (MessageBox.Show($"Update the server value for ALL the FILES in this ENVIRONMENT: '{currentDevEnv.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }

                foreach (var item in currentDevEnv.Files)
                {
                    updateServersByFile(item);
                }
            }
        }

        //private async void updateServersByFile(AliasItem _alias)
        //{
        //    try
        //    {
        //        var filePath = _alias.GetPath();

        //        if (!File.Exists(filePath))
        //        {
        //            StatusTextFileServers.Text = "File not found.";
        //            return;
        //        }

        //        var xml = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        //        var newServer = FileServersCombo.Text?.Trim() ?? "";
        //        var old = string.Empty;
        //        var updated = string.Empty;

        //        switch (_alias.Alias_Enum)
        //        {
        //            case AliasEnum.SdmAppWebConfig:
        //            case AliasEnum.SdmAppPubSubWebConfig:
        //            case AliasEnum.STSWebConfig:
        //                {
        //                    var conns = xml.Descendants().Where(x =>
        //                        string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
        //                        (x.Attribute("connectionString") != null)).Select(x => x);

        //                    foreach (var item in conns)
        //                    {
        //                        var name = item.Attribute("name")?.Value ?? "(unnamed)";
        //                        if (ignoreKeyList(name))
        //                            continue;

        //                        old = (string)item.Attribute("connectionString") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, string.Empty);
        //                        item.SetAttributeValue("connectionString", updated);
        //                    }

        //                    //SESSION STATE (DBSECURITY)
        //                    var addCustom = xml.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, "sessionState", StringComparison.OrdinalIgnoreCase) &&
        //                                             x.Attributes().Any(a => string.Equals(a.Name.LocalName, "mode", StringComparison.OrdinalIgnoreCase)) &&
        //                                             x.Parent != null && string.Equals(x.Parent.Name.LocalName, "system.web", StringComparison.OrdinalIgnoreCase));

        //                    if (addCustom != null)
        //                    {
        //                        old = (string)addCustom.Attribute("sqlConnectionString") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, string.Empty);
        //                        addCustom.SetAttributeValue("sqlConnectionString", updated);
        //                    }

        //                    break;
        //                }

        //            case AliasEnum.SdmAppBOMi2WebConfig:
        //            case AliasEnum.SdmAppPubSubBOMi2WebConfig:
        //                {
        //                    //ASMX/BOMI2
        //                    var addCustom2 = xml.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
        //                                             x.Attributes().Any(a => string.Equals(a.Name.LocalName, "key", StringComparison.OrdinalIgnoreCase)) &&
        //                                             x.Attribute("key").Value.Equals("DH2SQLServer"));

        //                    old = (string)addCustom2.Attribute("value") ?? "";
        //                    updated = UpdateConnectionString(old, newServer, string.Empty);
        //                    addCustom2.SetAttributeValue("value", updated);

        //                    break;
        //                }
        //            case AliasEnum.SdmAppMonitoringConfiguration:
        //                {
        //                    //PERFORMANCE - PROFILING (MonitoringConfiguration.xml)
        //                    var addCustom3 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "ProfileConsumer", StringComparison.OrdinalIgnoreCase) &&
        //                                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) &&
        //                                                            (a.Value.Equals("HttpRequestConsumer") || a.Value.Equals("HtmlCachingConsumer") || a.Value.Equals("MemberAccessConsumer"))));

        //                    foreach (var add in addCustom3)
        //                    {

        //                        var descendant = add.Descendants().Where(x => string.Equals(x.Name.LocalName, "Setting", StringComparison.OrdinalIgnoreCase) &&
        //                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) && a.Value.Equals("ConnectionString"))).FirstOrDefault();

        //                        old = (string)descendant.Attribute("value") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, string.Empty);
        //                        descendant.SetAttributeValue("value", updated);
        //                    }

        //                    break;
        //                }
        //            case AliasEnum.SdmLog4Net:
        //                {
        //                    //LOG4NET
        //                    var addsCustom4 = xml.Descendants("log4net").Where(x => string.Equals(x.Name.LocalName, "log4net", StringComparison.OrdinalIgnoreCase)).Descendants("appender")
        //                                        .Where(x => x.Attributes().Any(a => a.Name.LocalName.Equals("name") && a.Value.Equals("ErrorHandlerModule_SQLAppender")));

        //                    //LOG4NET
        //                    foreach (var add in addsCustom4)
        //                    {
        //                        var descendant = add.Descendants().Where(x => string.Equals(x.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase) &&
        //                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase))).FirstOrDefault();

        //                        old = (string)descendant.Attribute("value") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, string.Empty);
        //                        descendant.SetAttributeValue("value", updated);
        //                    }

        //                    break;
        //                }
        //        }

        //        xml.Save(filePath, SaveOptions.DisableFormatting);

        //        StatusTextFileServers.Text = "Saved!";
        //        await Task.Delay(3000);
        //        StatusTextFileServers.Text = string.Empty;
        //        LoadAliases();
        //        RefreshUI(_alias.Environment, false);
        //    }
        //    catch (Exception ex)
        //    {
        //        StatusTextFileServers.Text = "Error: " + ex.Message;
        //    }
        //}

        //private async void updateServersByFile(AliasItem _alias)
        //{
        //    try
        //    {
        //        var filePath = _alias.GetPath();

        //        if (!File.Exists(filePath))
        //        {
        //            StatusTextFileServers.Text = "File not found.";
        //            return;
        //        }

        //        var text = File.ReadAllText(filePath);
        //        var newServer = FileServersCombo.Text?.Trim() ?? "";

        //        // Helper to replace attribute values without touching whitespace
        //        string ReplaceAttribute(string xml, string elementName, string attributeName, Func<string, bool> filter, Func<string, string> updater)
        //        {
        //            return Regex.Replace(
        //                xml,
        //                $@"(<{elementName}\b[^>]*?\s{attributeName}\s*=\s*"")(.*?)("")",
        //                match =>
        //                {
        //                    var full = match.Value;
        //                    var nameMatch = Regex.Match(full, @"\bname\s*=\s*""([^""]*)""");
        //                    var name = nameMatch.Success ? nameMatch.Groups[1].Value : "(unnamed)";

        //                    if (!filter(name))
        //                        return match.Value;

        //                    var oldValue = match.Groups[2].Value;
        //                    var newValue = updater(oldValue);

        //                    return match.Groups[1].Value + newValue + match.Groups[3].Value;
        //                },
        //                RegexOptions.IgnoreCase | RegexOptions.Singleline
        //            );
        //        }

        //        switch (_alias.Alias_Enum)
        //        {
        //            case AliasEnum.SdmAppWebConfig:
        //            case AliasEnum.SdmAppPubSubWebConfig:
        //            case AliasEnum.STSWebConfig:
        //                text = ReplaceAttribute(
        //                    text,
        //                    "add",
        //                    "connectionString",
        //                    name => !ignoreKeyList(name),
        //                    old => UpdateConnectionString(old, newServer, string.Empty)
        //                );

        //                text = Regex.Replace(
        //                    text,
        //                    @"(<sessionState\b[^>]*?\ssqlConnectionString\s*=\s*"")(.*?)("")",
        //                    m => m.Groups[1].Value +
        //                         UpdateConnectionString(m.Groups[2].Value, newServer, string.Empty) +
        //                         m.Groups[3].Value,
        //                    RegexOptions.IgnoreCase
        //                );
        //                break;

        //            case AliasEnum.SdmAppBOMi2WebConfig:
        //            case AliasEnum.SdmAppPubSubBOMi2WebConfig:
        //                text = Regex.Replace(
        //                    text,
        //                    @"(<add\b[^>]*?\bkey\s*=\s*""DH2SQLServer""[^>]*?\bvalue\s*=\s*"")(.*?)("")",
        //                    m => m.Groups[1].Value +
        //                         UpdateConnectionString(m.Groups[2].Value, newServer, string.Empty) +
        //                         m.Groups[3].Value,
        //                    RegexOptions.IgnoreCase
        //                );
        //                break;

        //            case AliasEnum.SdmAppMonitoringConfiguration:
        //                text = Regex.Replace(
        //                    text,
        //                    @"(<Setting\b[^>]*?\bname\s*=\s*""ConnectionString""[^>]*?\bvalue\s*=\s*"")(.*?)("")",
        //                    m => m.Groups[1].Value +
        //                         UpdateConnectionString(m.Groups[2].Value, newServer, string.Empty) +
        //                         m.Groups[3].Value,
        //                    RegexOptions.IgnoreCase
        //                );
        //                break;

        //            case AliasEnum.SdmLog4Net:
        //                text = Regex.Replace(
        //                    text,
        //                    @"(<connectionString\b[^>]*?\bvalue\s*=\s*"")(.*?)("")",
        //                    m => m.Groups[1].Value +
        //                         UpdateConnectionString(m.Groups[2].Value, newServer, string.Empty) +
        //                         m.Groups[3].Value,
        //                    RegexOptions.IgnoreCase
        //                );
        //                break;
        //        }

        //        File.WriteAllText(filePath, text);

        //        StatusTextFileServers.Text = "Saved!";
        //        await Task.Delay(3000);
        //        StatusTextFileServers.Text = string.Empty;
        //        LoadAliases();
        //        RefreshUI(_alias.Environment, false);
        //    }
        //    catch (Exception ex)
        //    {
        //        StatusTextFileServers.Text = "Error: " + ex.Message;
        //    }
        //}

        private async void updateServersByFile(AliasItem _alias)
        {
            try
            {
                var filePath = _alias.GetPath();

                if (!File.Exists(filePath))
                {
                    StatusTextFileServers.Text = "File not found.";
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

                StatusTextFileServers.Text = "Saved!";
                await Task.Delay(3000);
                StatusTextFileServers.Text = string.Empty;

                LoadAliases();
                RefreshUI(_alias.Environment, false);
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
            //DatabaseCombo.ItemsSource = null;
            var dbs = new List<string>();
            serverAddress = serverAddress.TrimStart('\'').TrimEnd('\'');
            fileDb = fileDb.TrimStart('\'').TrimEnd('\'');

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
                DatabaseCombo.ItemsSource = null;
                DatabaseCombo.ItemsSource = dbs;
                if (!string.IsNullOrWhiteSpace(fileDb) && dbs.Any(x => x.ToLower().Equals(fileDb.ToLower())))
                {
                    var _item = dbs.Where(x => x.ToLower().Equals(fileDb.ToLower())).FirstOrDefault();
                    DatabaseCombo.SelectedItem = _item;
                }

                clearMessages();
            });
        }


        private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var server = ServerCombo.SelectedItem as string ?? ServerCombo.Text;
            var currentDb = DatabaseCombo.Text;
            PopulateDatabasesAsync(server, currentDb);
        }

        private void DatabaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            clearMessages();
        }

        #endregion

        #region Test connection

        //private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        //{
        //    ServerTestResult.Text = "";
        //    if (ServersListBox.SelectedItem == null) { ServerTestResult.Text = "Select a server to test connection."; return; }
        //    dynamic sel = ServersListBox.SelectedItem;
        //    var entry = sel.Entry as ServerEntry;
        //    if (entry == null) { ServerTestResult.Text = "Invalid selection."; return; }

        //    ServerTestResult.Text = "Testing...";
        //    var ok = await TestSqlConnectionAsync(entry);
        //    ServerTestResult.Text = ok ? "✅ Connection successful." : "❌ Connection failed (see console).";
        //    ServerTestResult.Foreground = ok ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        //}

        //private Task<bool> TestSqlConnectionAsync(ServerEntry entry)
        //{
        //    return Task.Run(() =>
        //    {
        //        try
        //        {
        //            var builder = new SqlConnectionStringBuilder
        //            {
        //                DataSource = entry.Address,
        //                InitialCatalog = "master",
        //                ConnectTimeout = 3,
        //                TrustServerCertificate = true
        //            };

        //            if (entry.UseSqlAuth)
        //            {
        //                builder.UserID = entry.Username;
        //                builder.Password = entry.Password;
        //                builder.IntegratedSecurity = false;
        //            }
        //            else
        //            {
        //                builder.IntegratedSecurity = true;
        //            }

        //            using var conn = new SqlConnection(builder.ConnectionString);
        //            conn.Open();
        //            conn.Close();
        //            return true;
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine("TestConn failed: " + ex.Message);
        //            return false;
        //        }
        //    });
        //}

        #endregion

        #region Save connection

        //private void BtnSaveConn_Click(object sender, RoutedEventArgs e)
        //{
        //    if (currentConn.name == null || currentConn.element == null || currentAlias == null)
        //    {
        //        StatusText.Text = "No connection selected.";
        //        return;
        //    }

        //    try
        //    {
        //        var filePath = currentAlias.GetPath();
        //        if (!File.Exists(filePath))
        //        {
        //            StatusText.Text = "File not found.";
        //            return;
        //        }

        //        var xml = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        //        var old = "";
        //        var newServer = ServerCombo.Text?.Trim() ?? "";
        //        var newDb = DatabaseCombo.Text?.Trim() ?? "";
        //        var updated = "";

        //        switch (currentAlias.Alias_Enum)
        //        {
        //            case AliasEnum.SdmAppWebConfig:
        //            case AliasEnum.SdmAppPubSubWebConfig:
        //            case AliasEnum.STSWebConfig:
        //                {
        //                    var add = xml.Descendants().FirstOrDefault(x =>
        //                        string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
        //                        string.Equals((string)x.Attribute("name") ?? "", currentConn.name, StringComparison.OrdinalIgnoreCase) &&
        //                        (x.Attribute("connectionString") != null));

        //                    //SESSION STATE (DBSECURITY)
        //                    var addCustom = xml.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, "sessionState", StringComparison.OrdinalIgnoreCase) &&
        //                                                           string.Equals((string)x.Attribute("mode") ?? "", currentConn.name, StringComparison.OrdinalIgnoreCase) &&
        //                                                           x.Parent != null && string.Equals(x.Parent.Name.LocalName, "system.web", StringComparison.OrdinalIgnoreCase));

        //                    if (add == null && addCustom == null)
        //                    {
        //                        StatusText.Text = "Connection entry not found in file.";
        //                        return;
        //                    }

        //                    if (add != null)
        //                    {
        //                        old = (string)add.Attribute("connectionString") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, newDb);
        //                        add.SetAttributeValue("connectionString", updated);
        //                    }

        //                    //SESSION STATE (DBSECURITY)
        //                    if (addCustom != null)
        //                    {
        //                        old = (string)addCustom.Attribute("sqlConnectionString") ?? "";
        //                        updated = UpdateConnectionString(old, newServer, newDb);
        //                        addCustom.SetAttributeValue("sqlConnectionString", updated);
        //                    }

        //                    break;
        //                }

        //            case AliasEnum.SdmAppBOMi2WebConfig:
        //            case AliasEnum.SdmAppPubSubBOMi2WebConfig:
        //                {
        //                    //ASMX/BOMI2
        //                    var addCustom2 = xml.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
        //                                                          x.Attributes().Any(a => string.Equals(a.Name.LocalName, "key", StringComparison.OrdinalIgnoreCase)) &&
        //                                                          x.Attribute("key").Value.Equals(currentConn.name));

        //                    if (addCustom2 == null)
        //                    {
        //                        StatusText.Text = "Connection entry not found in file.";
        //                        return;
        //                    }

        //                    old = (string)addCustom2.Attribute("value") ?? "";
        //                    updated = UpdateConnectionString(old, newServer, newDb);
        //                    addCustom2.SetAttributeValue("value", updated);

        //                    break;
        //                }
        //            case AliasEnum.SdmAppMonitoringConfiguration:
        //                {
        //                    //PERFORMANCE - PROFILING (MonitoringConfiguration.xml)
        //                    IEnumerable<XElement> addCustom3 = null;

        //                    if (currentConn.element.Parent.Parent.Attributes().Any(a => string.Equals(a.Name.LocalName, "name") && a.Value.Equals("HttpRequestConsumer")))
        //                    {
        //                        addCustom3 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "ProfileConsumer", StringComparison.OrdinalIgnoreCase) &&
        //                                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) &&
        //                                                            a.Value.Equals("HttpRequestConsumer")));
        //                    }
        //                    else if (currentConn.element.Parent.Parent.Attributes().Any(a => string.Equals(a.Name.LocalName, "name") && a.Value.Equals("HtmlCachingConsumer")))
        //                    {
        //                        addCustom3 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "ProfileConsumer", StringComparison.OrdinalIgnoreCase) &&
        //                                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) &&
        //                                                            a.Value.Equals("HtmlCachingConsumer")));
        //                    }
        //                    else if (currentConn.element.Parent.Parent.Attributes().Any(a => string.Equals(a.Name.LocalName, "name") && a.Value.Equals("MemberAccessConsumer")))
        //                    {
        //                        addCustom3 = xml.Descendants().Where(x => string.Equals(x.Name.LocalName, "ProfileConsumer", StringComparison.OrdinalIgnoreCase) &&
        //                                                             x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) &&
        //                                                             a.Value.Equals("MemberAccessConsumer")));
        //                    }

        //                    if (addCustom3 == null)
        //                    {
        //                        StatusText.Text = "Connection entry not found in file.";
        //                        return;
        //                    }

        //                    var descendant = addCustom3.Descendants().Where(x => string.Equals(x.Name.LocalName, "Setting", StringComparison.OrdinalIgnoreCase) &&
        //                                            x.Attributes().Any(a => string.Equals(a.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase) && a.Value.Equals("ConnectionString"))).FirstOrDefault();

        //                    old = (string)descendant.Attribute("value") ?? "";
        //                    updated = UpdateConnectionString(old, newServer, newDb);
        //                    descendant.SetAttributeValue("value", updated);

        //                    break;
        //                }
        //            case AliasEnum.SdmLog4Net:
        //                {
        //                    //LOG4NET
        //                    var addCustom4 = xml.Descendants("log4net").Where(x => string.Equals(x.Name.LocalName, "log4net", StringComparison.OrdinalIgnoreCase)).Descendants("appender")
        //                                        .FirstOrDefault(x => x.Attributes().Any(a => a.Name.LocalName.Equals("name") && a.Value.Equals("ErrorHandlerModule_SQLAppender")));

        //                    if (addCustom4 == null)
        //                    {
        //                        StatusText.Text = "Connection entry not found in file.";
        //                        return;
        //                    }

        //                    //LOG4NET
        //                    var descendant = addCustom4.Descendants().Where(x => string.Equals(x.Name.LocalName, "connectionString", StringComparison.OrdinalIgnoreCase) &&
        //                                        x.Attributes().Any(a => string.Equals(a.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase))).FirstOrDefault();

        //                    old = (string)descendant.Attribute("value") ?? "";
        //                    updated = UpdateConnectionString(old, newServer, newDb);
        //                    descendant.SetAttributeValue("value", updated);

        //                    break;
        //                }
        //        }

        //        xml.Save(filePath, SaveOptions.DisableFormatting);

        //        StatusText.Text = "Saved.";
        //        LoadAliases();
        //        RefreshUI(currentAlias.Environment, false);
        //    }
        //    catch (Exception ex)
        //    {
        //        StatusText.Text = "Error: " + ex.Message;
        //    }
        //}
        private void BtnSaveConn_Click(object sender, RoutedEventArgs e)
        {
            if (currentConn.name == null || currentConn.element == null || currentAlias == null)
            {
                StatusText.Text = "No connection selected.";
                return;
            }

            try
            {
                var filePath = currentAlias.GetPath();
                if (!File.Exists(filePath))
                {
                    StatusText.Text = "File not found.";
                    return;
                }

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
                                StatusText.Text = "Connection entry not found in file.";
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

                StatusText.Text = "Saved.";
                LoadAliases();
                RefreshUI(currentAlias.Environment, false);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
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
            if (!s.EndsWith(";")) s += ";";
            return s;
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

        #endregion
    }
}
