using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

[Transaction(TransactionMode.Manual)]
public class ClashDetection : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiApp = commandData.Application;
        UIDocument uiDoc = uiApp.ActiveUIDocument;
        Document doc = uiDoc.Document;

        try
        {
            // 选择要检测的类别
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType();

            List<Element> clashElements = new List<Element>();

            // 遍历元素进行碰撞检测
            foreach (Element elem1 in collector)
            {
                BoundingBoxXYZ bbox1 = elem1.get_BoundingBox(null);
                if (bbox1 == null) continue;

                BoundingBoxIntersectsFilter filter = new BoundingBoxIntersectsFilter(new Outline(bbox1.Min, bbox1.Max));
                FilteredElementCollector clashCollector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter);

                foreach (Element elem2 in clashCollector)
                {
                    if (elem1.Id != elem2.Id)
                    {
                        clashElements.Add(elem1);
                        clashElements.Add(elem2);
                    }
                }
            }

            // 高亮碰撞元素
            if (clashElements.Count > 0)
            {
                List<ElementId> clashIds = new List<ElementId>();
                foreach (Element e in clashElements)
                {
                    clashIds.Add(e.Id);
                }
                uiDoc.Selection.SetElementIds(clashIds);
            }

            TaskDialog.Show("Clash Detection", $"检测到 {clashElements.Count / 2} 组碰撞");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
