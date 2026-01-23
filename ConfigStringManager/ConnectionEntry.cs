using System.Windows;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;


namespace ConfigStringManager
{
    public class ConnectionEntry
    {
        public string Name { get; set; }
        public AliasItem Alias { get; set; }
        public XElement Element { get; set; }
        public Brush Foreground { get; set; }
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    }

}
