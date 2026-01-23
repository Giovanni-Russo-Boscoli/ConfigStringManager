using System.ComponentModel;

namespace ConfigStringManager
{
    public enum AliasEnum
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
}
