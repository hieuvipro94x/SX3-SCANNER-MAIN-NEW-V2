using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SX3_SCANER.Views.Controls
{
    public partial class ScanInputHistoryPanel : UserControl
    {
        public ScanInputHistoryPanel()
        {
            InitializeComponent();
            Loaded += ScanInputHistoryPanel_Loaded;
            Unloaded += ScanInputHistoryPanel_Unloaded;
        }

        private void ScanInputHistoryPanel_Loaded(object sender, RoutedEventArgs e)
        {
            ((INotifyCollectionChanged)ScanHistoryGrid.Items).CollectionChanged -= ScanHistory_CollectionChanged;
            ((INotifyCollectionChanged)ScanHistoryGrid.Items).CollectionChanged += ScanHistory_CollectionChanged;
            try
            {
                TbxInputCode.Focus();
                Keyboard.Focus(TbxInputCode);
            }
            catch
            {
            }
        }
        private void ScanInputHistoryPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            ((INotifyCollectionChanged)ScanHistoryGrid.Items).CollectionChanged -= ScanHistory_CollectionChanged;
        }

        private void ScanHistory_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Reset &&
                !(e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0))
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
            {
                if (IsLoaded && ScanHistoryGrid.Items.Count > 0)
                    ScanHistoryGrid.ScrollIntoView(ScanHistoryGrid.Items[0]);
            }));
        }
    }
}
