using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Test : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. 拾取目标栏杆
                Reference r = uidoc.Selection.PickObject(ObjectType.Element, "请选择要添加横杆的栏杆");
                Railing railing = doc.GetElement(r) as Railing;
                if (railing == null) return Result.Cancelled;

                using (Transaction t = new Transaction(doc, "实时写入多条横向扶栏"))
                {
                    t.Start();

                    // 2. 载入并激活截面族
                    string rfaPath = @"D:\test\test1\test1\截面.rfa";
                    Family fam = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Family))
                                    .Cast<Family>()
                                    .FirstOrDefault(f => f.Name == "截面");

                    if (fam == null)
                    {
                        if (!System.IO.File.Exists(rfaPath)) throw new Exception("磁盘找不到族文件: " + rfaPath);
                        doc.LoadFamily(rfaPath, out fam);
                    }

                    FamilySymbol profileSym = doc.GetElement(fam.GetFamilySymbolIds().First()) as FamilySymbol;
                    if (!profileSym.IsActive) profileSym.Activate();

                    // 3. 拿到栏杆类型的横杆“底层代理遥控器”
                    RailingType railingType = doc.GetElement(railing.GetTypeId()) as RailingType;
                    NonContinuousRailStructure railStruct = railingType.RailStructure;

                    // 4. 清空原有横杆 (从后往前删，直接操作底层数据)
                    for (int i = railStruct.GetNonContinuousRailCount() - 1; i >= 0; i--)
                    {
                        railStruct.RemoveNonContinuousRail(i);
                    }

                    // 5. 准备要生成的横杆高度数组 (对应你截图中的 700, 500, 300, 100)
                    double[] heightsMm = { 700.0, 300.0, 100.0 };

                    // 6. 实时遥控：动态添加新横杆
                    for (int i = 0; i < heightsMm.Length; i++)
                    {
                        // 计算符合 API 签名的 3 个必填参数
                        string railName = $"扶栏 {i + 1}";
                        double heightInFeet = heightsMm[i] / 304.8; // 毫米转英尺
                        double offsetInFeet = 0.0;

                        // 调用底层带参数的方法，直接写入 Revit 数据库！
                        NonContinuousRailInfo newRail = railStruct.AddNonContinuousRail(railName, heightInFeet, offsetInFeet);

                        // 给刚生成的这一行赋予轮廓 ID
                        newRail.ProfileId = profileSym.Id;
                    }

                    // 🚨 终极奥义：什么都不用 Set，直接重生成并提交！
                    //doc.Regenerate();
                    t.Commit();
                }

                // 强制显卡刷新，立即看到变化
                //uidoc.RefreshActiveView();
                TaskDialog.Show("成功", "多条横向扶栏已成功通过底层代理写入并生成！");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("运行报错", ex.Message + "\n" + ex.StackTrace);
                return Result.Failed;
            }
        }
    }
}