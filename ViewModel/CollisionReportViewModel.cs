using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevExpress.Mvvm;
using RevitCollisionDetection.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RevitCollisionDetection.ViewModel
{
    public class CollisionReportViewModel : ViewModelBase
    {
        private UIApplication _uiApp;
        private Document _doc;
        public ObservableCollection<CollisionInfo> CollisionResults { get; set; } = new ObservableCollection<CollisionInfo>();
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand HighlightCommand { get; }

        private List<string> CategoryList1;
        private List<string> CategoryList2;
        private bool _isSoftCollision;
        private double _distance;
        private string _selectedLevel;
        private double _selectedElevation = 0.0101;

        // 预缓存数据
        private Dictionary<BuiltInCategory, List<Element>> _elementCache;
        private Dictionary<ElementId, BoundingBoxXYZ> _boundingBoxCache;
        private Dictionary<ElementId, List<Solid>> _solidCache;

        public CollisionReportViewModel(UIApplication uiApp, List<string> categories1, List<string> categories2, bool isSoftCollision, double distance, string selectedLevel)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;
            CategoryList1 = categories1;
            CategoryList2 = categories2;
            _isSoftCollision = isSoftCollision;
            _distance = distance;
            _selectedLevel = selectedLevel;

            _elementCache = new Dictionary<BuiltInCategory, List<Element>>();
            _boundingBoxCache = new Dictionary<ElementId, BoundingBoxXYZ>();
            _solidCache = new Dictionary<ElementId, List<Solid>>();

            SetSelectedLevelElevation();
            CacheElementsAndBoundingBoxes();
            DetectCollisions();

            RefreshCommand = new DelegateCommand(RefreshCollisions);
            ExportCommand = new DelegateCommand(ExportReport);
            HighlightCommand = new DelegateCommand<CollisionInfo>(HighlightElements);
        }

        private void SetSelectedLevelElevation()
        {
            if (string.IsNullOrEmpty(_selectedLevel))
                return;

            var levelName = _selectedLevel.Split('(')[0].Trim(); // 处理格式: "1F (0.0m)"

            var level = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName);

            if (level != null)
                _selectedElevation = level.Elevation;
        }

        private void CacheElementsAndBoundingBoxes()
        {

            Parallel.ForEach(CategoryList1.Concat(CategoryList2).Distinct(), categoryName =>
            {
                BuiltInCategory bic = GetBuiltInCategory(categoryName);
                if (bic == BuiltInCategory.INVALID) return;
                var elements = new List<Element>();
                if (_selectedElevation == 0.0101)
                {
                    elements = new FilteredElementCollector(_doc)
                        .WherePasses(new ElementCategoryFilter(bic))
                        .WhereElementIsNotElementType()
                        .ToList();
                }
                else
                {
                    elements = new FilteredElementCollector(_doc)
                    .WherePasses(new ElementCategoryFilter(bic))
                    .WhereElementIsNotElementType()
                    .Where(e => IsElementBelowSelectedLevel(e)) // 过滤低于选定标高的构件
                    .ToList();
                }

                lock (_elementCache) // 确保线程安全
                {
                    _elementCache[bic] = elements;
                }
                if (_selectedElevation == 0.0101)
                {
                    foreach (var elem in elements)
                    {
                        var bbox = elem.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            lock (_boundingBoxCache)
                            {
                                _boundingBoxCache[elem.Id] = bbox;
                            }
                        }
                    }
                }
            });
        }

        private static CollisionInfo _selectedCollision;
        public CollisionInfo SelectedCollision
        {
            get => _selectedCollision;
            set
            {
                if (_selectedCollision != value)
                {
                    _selectedCollision = value;
                    RaisePropertyChanged(nameof(SelectedCollision));
                    HighlightCommand.Execute(_selectedCollision);
                }
            }
        }

        private BuiltInCategory GetBuiltInCategory(string categoryName)
        {
            var category = _doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c => c.Name == categoryName);

            return category != null ? (BuiltInCategory)category.Id.IntegerValue : BuiltInCategory.INVALID;
        }

        private void DetectCollisions()
        {
            var reportedCollisions = new HashSet<(ElementId, ElementId)>();
            int countResult = 0;

            foreach (var cat1 in CategoryList1)
            {
                if (!_elementCache.TryGetValue(GetBuiltInCategory(cat1), out var elements1))
                    continue;

                foreach (var cat2 in CategoryList2)
                {
                    if (!_elementCache.TryGetValue(GetBuiltInCategory(cat2), out var elements2))
                        continue;

                    bool sameCategory = (cat1 == cat2);

                    for (int i = 0; i < elements1.Count; i++)
                    {
                        var elem1 = elements1[i];
                        if (!_boundingBoxCache.TryGetValue(elem1.Id, out var bbox1))
                            continue;

                        int startIndex = sameCategory ? i + 1 : 0; // 避免相同类别重复对比

                        for (int j = startIndex; j < elements2.Count; j++)
                        {
                            var elem2 = elements2[j];

                            if (!_boundingBoxCache.TryGetValue(elem2.Id, out var bbox2))
                                continue;

                            // 避免重复计算
                            if (!reportedCollisions.Add((elem1.Id, elem2.Id)))
                                continue;

                            bool isCollision = _isSoftCollision
                                ? CheckSoftCollision(bbox1, bbox2, _distance)
                                : CheckHardCollision(bbox1, bbox2) && CheckElementCollisionWithSolid(elem1, elem2);

                            if (isCollision)
                            {
                                CollisionResults.Add(new CollisionInfo
                                {
                                    DisplayText = $"序号：{countResult}, {elem1.Category.Name}:{elem1.Id} 与 {elem2.Category.Name}:{elem2.Id} 发生碰撞",
                                    Element1 = elem1.Id,
                                    Element2 = elem2.Id
                                });
                                countResult++;
                            }
                        }
                    }
                }
            }
        }
        private bool CheckSoftCollision(BoundingBoxXYZ bbox1, BoundingBoxXYZ bbox2, double distance)
        {
            return !(bbox1.Max.X + distance < bbox2.Min.X || bbox1.Min.X - distance > bbox2.Max.X ||
                     bbox1.Max.Y + distance < bbox2.Min.Y || bbox1.Min.Y - distance > bbox2.Max.Y ||
                     bbox1.Max.Z + distance < bbox2.Min.Z || bbox1.Min.Z - distance > bbox2.Max.Z);
        }


        private bool IsElementBelowSelectedLevel(Element element)
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox != null && bbox.Min.Z <= _selectedElevation)
            {
                lock (_boundingBoxCache)
                {
                    _boundingBoxCache[element.Id] = bbox;
                }
                return true;
            }

            return false;
        }

        private void HighlightElements(CollisionInfo collision)
        {
            if (collision == null) return;

            var uidoc = _uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            try
            {
                var selection = uidoc.Selection;
                selection.SetElementIds(new[] { collision.Element1, collision.Element2 });
                uidoc.ShowElements(new[] { collision.Element1, collision.Element2 });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighlightElements 发生错误: {ex.Message}");
            }
        }

        private bool CheckHardCollision(BoundingBoxXYZ bbox1, BoundingBoxXYZ bbox2)
        {
            return !(bbox1.Max.X < bbox2.Min.X || bbox1.Min.X > bbox2.Max.X ||
                     bbox1.Max.Y < bbox2.Min.Y || bbox1.Min.Y > bbox2.Max.Y ||
                     bbox1.Max.Z < bbox2.Min.Z || bbox1.Min.Z > bbox2.Max.Z);
        }

        private bool CheckElementCollisionWithSolid(Element elem1, Element elem2)
        {
            if (!_solidCache.TryGetValue(elem1.Id, out var solids))
            {
                Options geomOptions = new Options { ComputeReferences = true };
                var geomElem = elem1.get_Geometry(geomOptions);
                solids = geomElem.OfType<Solid>().Where(s => s.Volume > 0).ToList();
                _solidCache[elem1.Id] = solids;  // 缓存计算结果
            }

            if (!solids.Any()) return false;

            var collector = new FilteredElementCollector(_doc, new List<ElementId> { elem2.Id });
            return solids.Any(solid => collector.WherePasses(new ElementIntersectsSolidFilter(solid)).FirstOrDefault() != null);
        }

        private void RefreshCollisions()
        {
            CollisionResults.Clear();
            CacheElementsAndBoundingBoxes();
            DetectCollisions();
        }

        private void ExportReport()
        {
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CollisionReport.txt");
            File.WriteAllLines(filePath, CollisionResults.Select(r => r.DisplayText).Prepend("碰撞检测报告\n======================"));
            TaskDialog.Show("导出完成", "碰撞检测报告已导出到桌面。");
        }
    }
}
