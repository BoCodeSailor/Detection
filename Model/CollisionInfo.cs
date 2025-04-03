using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitCollisionDetection.Model
{
    public class CollisionInfo
    {
        public string DisplayText { get; set; }  // 界面显示的文本
        public ElementId Element1 { get; set; }  // 碰撞元素1
        public ElementId Element2 { get; set; }  // 碰撞元素2
    }
}
