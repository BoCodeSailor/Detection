using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;
using RevitCollisionDetection.ViewModel;

namespace RevitCollisionDetection.View
{
    public partial class CollisionReportView : Window
    {
        public CollisionReportView(UIApplication uiApp, List<string> categories1, List<string> categories2, bool isSoftCollision, double distance, string selectedLevel)
        {
            InitializeComponent();
            DataContext = new CollisionReportViewModel(uiApp, categories1, categories2, isSoftCollision, distance, selectedLevel);
            IntPtr revitHandle = uiApp.MainWindowHandle;
            System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = revitHandle;
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
