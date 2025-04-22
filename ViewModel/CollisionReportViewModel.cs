using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClassLibrary1.Model;
using DevExpress.Mvvm;
using Microsoft.SqlServer.Server;
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
        int _isHardCollision;
        double _minDistance = 0;

        // 预缓存数据
        private Dictionary<BuiltInCategory, List<Element>> _elementCache;
        private Dictionary<ElementId, BoundingBoxXYZ> _boundingBoxCache;
        private Dictionary<ElementId, List<Solid>> _solidCache;

        List<XYZ> directions = new List<XYZ>
        {
            XYZ.BasisX,
            XYZ.BasisY,
            XYZ.BasisZ,
            new XYZ(1, 1, 1).Normalize(),
            new XYZ(-1, -1, -1).Normalize()
        };


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
            Logger.ClearLog();

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
                                ? CheckSoftCollision(bbox1, bbox2, _distance) && CheckSoftCollisionPreciseSafe(elem1, elem2, _distance)
                                : CheckHardCollision(bbox1, bbox2) && CheckElementCollisionWithSolid(elem1, elem2);

                            if (isCollision)
                            {
                                if (_isSoftCollision)
                                {
                                    CollisionResults.Add(new CollisionInfo
                                    {
                                        DisplayText = $"序号：{countResult}, {elem1.Category.Name}:{elem1.Id} 与 {elem2.Category.Name}:{elem2.Id} 发生碰撞，碰撞类型（0软，1硬）：{_isHardCollision}, 间隔：{_minDistance * 304.8} mm",
                                        Element1 = elem1.Id,
                                        Element2 = elem2.Id
                                    });
                                }
                                else
                                {
                                    CollisionResults.Add(new CollisionInfo
                                    {
                                        DisplayText = $"序号：{countResult}, {elem1.Category.Name}:{elem1.Id} 与 {elem2.Category.Name}:{elem2.Id} 发生碰撞",
                                        Element1 = elem1.Id,
                                        Element2 = elem2.Id
                                    });
                                }
                                countResult++;
                            }
                        }

                    }
                }
            }
        }

        private bool CheckSoftCollision(BoundingBoxXYZ bbox1, BoundingBoxXYZ bbox2, double distance)
        {
            distance = distance / 304.8;
            return !(bbox1.Max.X + distance < bbox2.Min.X || bbox1.Min.X - distance > bbox2.Max.X ||
                     bbox1.Max.Y + distance < bbox2.Min.Y || bbox1.Min.Y - distance > bbox2.Max.Y ||
                     bbox1.Max.Z + distance < bbox2.Min.Z || bbox1.Min.Z - distance > bbox2.Max.Z);
        }

        private bool CheckSoftCollisionPreciseSafe(Element elem1, Element elem2, double distanceThreshold)
        {
            try
            {
                distanceThreshold = distanceThreshold / 304.8;
                double tolerance = _doc.Application.ShortCurveTolerance;
                Logger.Log($"[INFO] Start checking soft collision between {elem1.Id} and {elem2.Id}, threshold: {distanceThreshold}，tolerance：{tolerance}");
                if (CheckElementCollisionWithSolid(elem1, elem2))
                {
                    _isHardCollision = 1;
                    _minDistance = 0;
                    Logger.Log("[INFO] Hard collision detected");
                    return true;
                }
                var solids1 = GetElementSolidsSafe(elem1);
                var solids2 = GetElementSolidsSafe(elem2);

                if (solids1.Count == 0 || solids2.Count == 0)
                {
                    Logger.Log($"[WARN] One or both elements have no valid solids: {elem1.Id}, {elem2.Id}");
                    return false;
                }

                //foreach (var solid1 in solids1)
                //{
                //    foreach (var solid2 in solids2)
                //    {
                //        try
                //        {
                //            var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
                //            if (intersection != null && intersection.Volume > tolerance)
                //            {
                //                _isHardCollision = 1;
                //                _minDistance = 0;
                //                Logger.Log("[INFO] Hard collision detected");
                //                return true;
                //            }
                //        }
                //        catch (Exception ex)
                //        {
                //            Logger.Log($"[ERROR] Boolean operation failed: {ex.Message}");
                //        }
                //    }
                //}

                foreach (var solid1 in solids1)
                {
                    foreach (var solid2 in solids2)
                    {
                        double minDistance = ComputeSolidsMinDistanceSafe(solid1, solid2, tolerance);
                        if (minDistance <= distanceThreshold)
                        {
                            _isHardCollision = 0;
                            _minDistance = minDistance;
                            Logger.Log($"[INFO] Soft collision detected. Min distance: {minDistance}");
                            return true;
                        }
                    }
                }

                Logger.Log("[INFO] No collision detected.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FATAL] Soft collision check failed: {ex.Message}");
                return false;
            }
        }

        private List<Solid> GetElementSolidsSafe(Element elem)
        {
            if (!_solidCache.TryGetValue(elem.Id, out var solids))
            {
                solids = new List<Solid>();
                Options geomOptions = new Options { ComputeReferences = true };
                var geomElem = elem.get_Geometry(geomOptions);

                if (geomElem == null)
                {
                    Logger.Log($"[WARN] Null geometry for element {elem.Id}");
                    return solids;
                }

                solids = geomElem.OfType<Solid>().Where(s => s.Volume > 0).ToList();
                _solidCache[elem.Id] = solids;
            }
            Logger.Log($"[INFO] Element {elem.Id} has {solids.Count} valid solids.");
            return solids;
        }

        private double ComputeSolidsMinDistanceSafe(Solid solid1, Solid solid2, double tolerance)
        {
            double minDistance = double.MaxValue;
            int maxFaces = 100;
            int faceIndex = 0;

            foreach (Face face in solid1.Faces)
            {
                if (faceIndex++ >= maxFaces)
                {
                    Logger.Log("[WARN] Face limit reached, skipping remaining.");
                    break;
                }

                var points = SampleFacePointsSafe(face);
                foreach (XYZ point in points.Take(40)) // 限制最多取40个点
                {
                    double dist = ComputeMinDistanceToSolidSafe(point, solid2, tolerance);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        //Logger.Log($"[INFO]. Min distance: {minDistance}");
                        if (minDistance <= 0) return 0;
                    }
                }
            }

            return minDistance;
        }

        private List<XYZ> SampleFacePointsSafe(Face face)
        {
            List<XYZ> points = new List<XYZ>();
            BoundingBoxUV bb = face.GetBoundingBox();

            double uRange = bb.Max.U - bb.Min.U;
            double vRange = bb.Max.V - bb.Min.V;
            int maxTotalPoints = 50;
            double baseStep = 0.05;

            int uSteps = Math.Max((int)(uRange / baseStep), 2);
            int vSteps = Math.Max((int)(vRange / baseStep), 2);
            int totalPoints = uSteps * vSteps;

            if (totalPoints > maxTotalPoints)
            {
                double scale = Math.Sqrt((double)maxTotalPoints / totalPoints);
                uSteps = Math.Max((int)(uSteps * scale), 2);
                vSteps = Math.Max((int)(vSteps * scale), 2);
            }

            double uStep = uSteps > 1 ? uRange / (uSteps - 1) : uRange;
            double vStep = vSteps > 1 ? vRange / (vSteps - 1) : vRange;

            if (double.IsNaN(uStep) || double.IsInfinity(uStep) ||
                double.IsNaN(vStep) || double.IsInfinity(vStep))
            {
                Logger.Log("[ERROR] Invalid UV step values. Sampling skipped.");
                return points;
            }

            for (int i = 0; i < uSteps; i++)
            {
                double u = bb.Min.U + i * uStep;
                for (int j = 0; j < vSteps; j++)
                {
                    double v = bb.Min.V + j * vStep;
                    try
                    {
                        XYZ point = face.Evaluate(new UV(u, v));
                        points.Add(point);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            Shuffle(points);
            //Logger.Log($"[INFO] Sampled {points.Count} points from face.");
            return points;
        }

        private void Shuffle<T>(IList<T> list)
        {
            Random rng = new Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        private double ComputeMinDistanceToSolidSafe(XYZ point, Solid solid, double tolerance)
        {
            if (IsPointInsideSolidSafe(solid, point, tolerance))
            {
                Logger.Log($"[INFO] IsPointInsideSolid.");
                return 0;
            }

            double minDist = double.MaxValue;
            foreach (Face face in solid.Faces)
            {
                var result = face.Project(point);
                if (result == null) continue;
                double dist = point.DistanceTo(result.XYZPoint);
                if (dist < minDist)
                    minDist = dist;
            }
            return minDist;
        }

        private bool IsPointInsideSolidSafe(Solid solid, XYZ point, double tolerance)
        {
            int insideCount = 0;
            int faceCount = 0;

            foreach (Face face in solid.Faces)
            {
                faceCount++;
                var result = face.Project(point);
                if (result != null && result.Distance < tolerance)
                {
                    insideCount++;
                }
            }

            return faceCount > 0 && insideCount > faceCount * 0.6;
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

            try
            {
                var uidoc = _uiApp.ActiveUIDocument;
                var doc = uidoc.Document;
                var elementIds = new[] { collision.Element1, collision.Element2 };

                // 验证元素是否存在
                var validElements = elementIds
                    .Select(id => doc.GetElement(id))
                    .Where(el => el != null)
                    .Select(el => el.Id)
                    .ToList();

                if (!validElements.Any()) return;

                // 切换到非模板 3D 视图（仅当当前视图不支持 ShowElements）
                var currentView = doc.ActiveView;
                if (!(currentView is View3D) || currentView.IsTemplate)
                {
                    var view3D = new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.ThreeD);

                    if (view3D != null)
                    {
                        uidoc.ActiveView = view3D;
                    }
                }

                // 聚焦元素（更显眼地定位）
                uidoc.ShowElements(validElements);
                uidoc.Selection.SetElementIds(validElements);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighlightElements 发生错误: {ex}");
                Logger.Log($"[ERROR] HighlightElements 异常：{ex}");
            }
        }
        private bool CheckHardCollision(BoundingBoxXYZ bbox1, BoundingBoxXYZ bbox2)
        {
            // 先检查 XY 平面，避免不必要的 Z 计算
            if (bbox1.Max.X < bbox2.Min.X || bbox1.Min.X > bbox2.Max.X ||
                bbox1.Max.Y < bbox2.Min.Y || bbox1.Min.Y > bbox2.Max.Y)
            {
                return false;
            }

            // 再检查 Z 轴
            return !(bbox1.Max.Z < bbox2.Min.Z || bbox1.Min.Z > bbox2.Max.Z);
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
