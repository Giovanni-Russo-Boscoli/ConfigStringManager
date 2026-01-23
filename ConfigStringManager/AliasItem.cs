using Enums.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;

namespace ConfigStringManager
{
    public class AliasItem
    {
        public AliasEnum Alias_Enum { get; set; }
        public string Alias => Alias_Enum.GetDescription();
        public string SufixPath { get; set; }
        public string EnvironmentName { get; set; }   // NEW
        public bool IsSTS { get; set; }
        [JsonIgnore]
        public ObservableCollection<object> Children { get; } = new();

        public string GetPath(DevEnvironment env)
        {
            if (IsSTS)
                return Path.Combine(env.STSPrefixPath, SufixPath);

            return Path.Combine(env.PrefixPath, SufixPath);
        }
    }
}
