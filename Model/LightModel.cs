using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCollisionDetection.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

public class HighlightCollisionHandler : IExternalEventHandler
{
    private UIApplication _uiApp;
    private CollisionInfo _collision;

    public void Init(UIApplication uiApp, CollisionInfo collision)
    {
        _uiApp = uiApp;
        _collision = collision;
    }

    public void Execute(UIApplication app)
    {
        try
        {
            var doc = _uiApp.ActiveUIDocument.Document;
            var uidoc = _uiApp.ActiveUIDocument;

            var elementIds = new[] { _collision.Element1, _collision.Element2 };
            var elements = elementIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            if (elements.Count == 0) return;

            var boxes = elements.Select(e => e.get_BoundingBox(doc.ActiveView)).Where(bb => bb != null).ToList();
            if (boxes.Count == 0) return;

            var min = boxes.Select(bb => bb.Min).Aggregate((a, b) => new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
            var max = boxes.Select(bb => bb.Max).Aggregate((a, b) => new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

            double offset = 2;
            min -= new XYZ(offset, offset, offset);
            max += new XYZ(offset, offset, offset);

            var sectionBox = new BoundingBoxXYZ
            {
                Min = min,
                Max = max,
                Enabled = true
            };

            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

            if (viewFamilyType == null)
            {
                TaskDialog.Show("错误", "未找到剖面图类型");
                return;
            }

            using (var tx = new Transaction(doc, "创建剖面图"))
            {
                tx.Start();
                var section = ViewSection.CreateSection(doc, viewFamilyType.Id, sectionBox);
                tx.Commit();

                uidoc.ActiveView = section;
                uidoc.Selection.SetElementIds(elements.Select(e => e.Id).ToList());
            }
        }
        catch (Exception ex)
        {
            TaskDialog.Show("异常", $"HighlightCollisionHandler 执行异常: {ex.Message}");
        }
    }

    public string GetName() => "Highlight Collision Handler";
}
