using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevExpress.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;
using RevitCollisionDetection.View;
using System.ComponentModel;

namespace RevitCollisionDetection.ViewModel
{
    public class CollisionDetectionViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private UIApplication _uiApp;
        private Document _doc;

        public ObservableCollection<string> Categories { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> SelectedCategories1 { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> SelectedCategories2 { get; set; } = new ObservableCollection<string>();

        public bool IsHardCollision { get; set; } = true;
        private bool _isSoftCollision;
        public ObservableCollection<string> Levels { get; set; } = new ObservableCollection<string>();
        private string _selectedLevel;
        public string SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (_selectedLevel != value)
                {
                    _selectedLevel = value;
                    OnPropertyChanged(nameof(SelectedLevel));
                }
            }
        }
        public bool IsSoftCollision
        {
            get => _isSoftCollision;
            set
            {
                if (_isSoftCollision != value)
                {
                    _isSoftCollision = value;
                    OnPropertyChanged(nameof(IsSoftCollision)); // 手动通知 UI
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public double SoftCollisionDistance { get; set; } = 0.0;

        public ICommand StartDetectionCommand { get; }

        public CollisionDetectionViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;

            LoadCategories();
            LoadLevels();
            StartDetectionCommand = new DelegateCommand(StartDetection);
        }

        //private void LoadCategories()
        //{
        //    var categories = _doc.Settings.Categories
        //        .Cast<Category>()
        //        .Where(c => c.CategoryType == CategoryType.Model && !c.Name.Contains("分析")) // 过滤掉非几何模型
        //        .Select(c => c.Name);

        //    foreach (var cat in categories)
        //    {
        //        Categories.Add(cat);
        //    }
        //}
        private void LoadLevels()
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation) // 按高度排序
                .Select(l => $"{l.Name} ({l.Elevation}m)")
                .ToList();

            Levels.Clear();
            Levels.Add("无");  // 添加"无"选项
            foreach (var level in levels)
            {
                Levels.Add(level);
            }

            SelectedLevel = "无"; // 默认选择“无”
        }
        private void LoadCategories()
        {
            var categorySet = new HashSet<string>();

            var elementsInView = new FilteredElementCollector(_doc, _doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var elem in elementsInView)
            {
                Category category = elem.Category;
                if (category != null && category.CategoryType == CategoryType.Model && !category.Name.Contains("分析"))
                {
                    // 通过 Geometry 进一步筛选，确保是可见的几何元素
                    GeometryElement geomElem = elem.get_Geometry(new Options());
                    if (geomElem != null && geomElem.Cast<GeometryObject>().Any())
                    {
                        categorySet.Add(category.Name);
                    }
                }
            }

            // 清空并填充类别列表
            Categories.Clear();
            foreach (var categoryName in categorySet)
            {
                Categories.Add(categoryName);
            }
        }

        private void StartDetection()
        {
            if (SelectedCategories1 == null || !SelectedCategories1.Any() ||
                SelectedCategories2 == null || !SelectedCategories2.Any())
            {
                TaskDialog.Show("错误", "请选择至少一个类别进行碰撞检测。");
                return;
            }

            if (IsSoftCollision && SoftCollisionDistance <= 0)
            {
                TaskDialog.Show("错误", "请设置一个有效的软碰撞间距。");
                return;
            }

            string selectedLevelToPass = SelectedLevel == "无" ? null : SelectedLevel; // 传递 null 代表不过滤标高

            var collisionWindow = new CollisionReportView(_uiApp, SelectedCategories1.ToList(), SelectedCategories2.ToList(), IsSoftCollision, SoftCollisionDistance, selectedLevelToPass);
            collisionWindow.Show();
        }
    }
}

