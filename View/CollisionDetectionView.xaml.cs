
using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using RevitCollisionDetection.ViewModel;

namespace RevitCollisionDetection.View
{
    public partial class CollisionDetectionView : Window
    {
        public CollisionDetectionView(UIApplication uiApp)
        {
            InitializeComponent();
            IntPtr revitHandle = uiApp.MainWindowHandle;
            System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = revitHandle;
            DataContext = new CollisionDetectionViewModel(uiApp);
        }
        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void ListBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is CollisionDetectionViewModel viewModel)
            {
                viewModel.SelectedCategories1.Clear();
                foreach (var item in ListBox1.SelectedItems)
                {
                    viewModel.SelectedCategories1.Add(item.ToString());
                }
            }
        }

        private void ListBox2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is CollisionDetectionViewModel viewModel)
            {
                viewModel.SelectedCategories2.Clear();
                foreach (var item in ListBox2.SelectedItems)
                {
                    viewModel.SelectedCategories2.Add(item.ToString());
                }
            }
        }
    }
}
