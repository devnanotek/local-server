using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>Tüm sayfaların temel sınıfı.</summary>
    public class ViewBase : UserControl
    {
        public virtual void Refresh() { }
        public virtual void OnStatus(StackStatus s) { }
        protected MainWindow Main => MainWindow.Instance;
        protected AppConfig Cfg => AppConfig.Current;
    }
}
