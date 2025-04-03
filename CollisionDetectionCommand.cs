using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCollisionDetection.View;

namespace RevitCollisionDetection
{
    [Transaction(TransactionMode.Manual)]
    public class CollisionDetectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            // 打开碰撞检测选择界面
            CollisionDetectionView window = new CollisionDetectionView(uiApp);
            window.Show();

            return Result.Succeeded;
        }
    }
}
