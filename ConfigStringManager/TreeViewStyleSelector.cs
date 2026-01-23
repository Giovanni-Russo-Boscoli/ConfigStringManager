using System.Windows;
using System.Windows.Controls;

namespace ConfigStringManager
{
    public class TreeViewStyleSelector : StyleSelector
    {
        public Style EnvStyle { get; set; }
        public Style AliasStyle { get; set; }
        public Style ConnStyle { get; set; }
        public Style MsgStyle { get; set; }

        public override Style SelectStyle(object item, DependencyObject container)
        {
            if (item is DevEnvironment)
                return EnvStyle;

            if (item is AliasItem)
                return AliasStyle;

            if (item is ConnectionEntry)
                return ConnStyle;

            if (item is MissingFileMessage)
                return MsgStyle;

            return base.SelectStyle(item, container);
        }
    }
}
