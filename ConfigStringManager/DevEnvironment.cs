using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ConfigStringManager
{
    public class DevEnvironment
    {
        public string Name { get; set; }
        public string PrefixPath { get; set; }
        public string STSPrefixPath { get; set; }

        [JsonIgnore]
        public ObservableCollection<AliasItem> Files { get; } = new();

        public void EnsureFilesInitialized()
        {
            if (Files.Count > 0)
                return;

            var list = GetEnvironmentFiles(); 
            foreach (var a in list)
                Files.Add(a);
        }

        public IList<AliasItem> GetEnvironmentFiles()
        {
            return new List<AliasItem>
                    {
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmAppWebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Web.config", EnvironmentName = Name},
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmAppBOMi2WebConfig, SufixPath = "Code\\DELIVERY\\server\\asmx\\Web-bomi2AppSettings.config", EnvironmentName = Name },
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmAppPubSubWebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App.PubSub\\Web.config",EnvironmentName = Name },
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmAppPubSubBOMi2WebConfig, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App.PubSub\\bomi2\\server\\asmx\\Web-bomi2AppSettings.config", EnvironmentName = Name },
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmAppMonitoringConfiguration, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Config\\MonitoringConfiguration.xml", EnvironmentName = Name },
                        new AliasItem(){ Alias_Enum = AliasEnum.SdmLog4Net, SufixPath = "Code\\DELIVERY\\business4\\Sdm.App\\Sdm.App\\Config\\Sdm.Log4Net.xml", EnvironmentName = Name },
                        new AliasItem(){ IsSTS = true,  Alias_Enum = AliasEnum.STSWebConfig, SufixPath = "Sdm.App.STS\\Web.config", EnvironmentName = Name }
                    };
        }
    }

}
