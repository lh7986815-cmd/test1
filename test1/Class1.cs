
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitAddin
{
    // 1. 过滤器
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    // 2. 坡度类型枚举
    public enum SlopeType
    {
        FourWay, // 四分找坡
        TwoWay,  // 二分找坡
        OneWay   // 单向坡
    }

    // 3. 动态生成的 WinForms 弹窗 UI
    // 3. 动态生成的 WinForms 弹窗 UI
    public class SlopeConfigForm : System.Windows.Forms.Form
    {
        public SlopeType SelectedSlopeType { get; private set; }
        private System.Windows.Forms.ComboBox cmbSlopeType;
        private System.Windows.Forms.Button btnOk;

        public SlopeConfigForm()
        {
            this.Text = "屋顶排水找坡设置";
            // 【修复】：显式使用 System.Drawing.Size
            this.Size = new System.Drawing.Size(300, 150);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            System.Windows.Forms.Label lbl = new System.Windows.Forms.Label();
            lbl.Text = "请选择找坡模式 (默认 2% 坡度):";
            // 【修复】：显式使用 System.Drawing.Point
            lbl.Location = new System.Drawing.Point(20, 20);
            lbl.AutoSize = true;
            this.Controls.Add(lbl);

            cmbSlopeType = new System.Windows.Forms.ComboBox();
            cmbSlopeType.Items.Add("四分找坡 (向四周排水)");
            cmbSlopeType.Items.Add("二分找坡 (向两侧排水)");
            cmbSlopeType.Items.Add("单向坡 (向单侧排水)");
            cmbSlopeType.SelectedIndex = 0; // 默认选第一个
            // 【修复】：显式使用 System.Drawing.Point
            cmbSlopeType.Location = new System.Drawing.Point(20, 45);
            cmbSlopeType.Width = 240;
            cmbSlopeType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Controls.Add(cmbSlopeType);

            btnOk = new System.Windows.Forms.Button();
            btnOk.Text = "确定生成";
            // 【修复】：显式使用 System.Drawing.Point
            btnOk.Location = new System.Drawing.Point(185, 75);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            SelectedSlopeType = (SlopeType)cmbSlopeType.SelectedIndex;
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }
    }

    // 4. 主命令逻辑
    [Transaction(TransactionMode.Manual)]
    public class Test : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 第一步：让用户选择楼板
                Reference pickedRef = uidoc.Selection.PickObject(ObjectType.Element, new FloorSelectionFilter(), "请选择一块矩形楼板进行找坡");
                Floor floor = doc.GetElement(pickedRef) as Floor;
                if (floor == null) return Result.Cancelled;

                // 第二步：弹出 UI 让用户选择模式
                SlopeType chosenType = SlopeType.FourWay;
                using (SlopeConfigForm form = new SlopeConfigForm())
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        return Result.Cancelled; // 用户关掉了窗口
                    }
                    chosenType = form.SelectedSlopeType;
                }

                // 第三步：执行生成逻辑
                using (Transaction trans = new Transaction(doc, "生成屋顶找坡"))
                {
                    trans.Start();

                    SlabShapeEditor editor = floor.GetSlabShapeEditor();
                    editor.ResetSlabShape();
                    if (!editor.IsEnabled) editor.Enable();
                    doc.Regenerate();

                    BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                    XYZ min = bbox.Min;
                    XYZ max = bbox.Max;

                    double lengthX = max.X - min.X;
                    double lengthY = max.Y - min.Y;
                    bool isXLonger = lengthX >= lengthY;

                    double slope = 0.02; // 统一使用 2% 坡度

                    // 根据用户的选择，走不同的几何逻辑
                    if (chosenType == SlopeType.FourWay)
                    {
                        // === 四分找坡 ===
                        double shortSideHalf = (isXLonger ? lengthY : lengthX) / 2.0;
                        double ridgeHeight = shortSideHalf * slope;

                        XYZ p1 = isXLonger ? new XYZ(min.X + shortSideHalf, min.Y + shortSideHalf, min.Z)
                                           : new XYZ(min.X + shortSideHalf, min.Y + shortSideHalf, min.Z);
                        XYZ p2 = isXLonger ? new XYZ(max.X - shortSideHalf, min.Y + shortSideHalf, min.Z)
                                           : new XYZ(min.X + shortSideHalf, max.Y - shortSideHalf, min.Z);

                        SlabShapeVertex v1 = editor.AddPoint(p1);
                        SlabShapeVertex v2 = editor.AddPoint(p2);
                        editor.ModifySubElement(v1, ridgeHeight);
                        editor.ModifySubElement(v2, ridgeHeight);
                        editor.AddSplitLine(v1, v2);
                    }
                    else if (chosenType == SlopeType.TwoWay)
                    {
                        // === 二分找坡 === (平行于长边起脊，向两边短边排水)
                        double midY = min.Y + lengthY / 2.0;
                        double midX = min.X + lengthX / 2.0;

                        double ridgeHeight = (isXLonger ? lengthY : lengthX) / 2.0 * slope;

                        // 起脊线的端点落在边界上
                        XYZ p1 = isXLonger ? new XYZ(min.X, midY, min.Z) : new XYZ(midX, min.Y, min.Z);
                        XYZ p2 = isXLonger ? new XYZ(max.X, midY, min.Z) : new XYZ(midX, max.Y, min.Z);

                        SlabShapeVertex v1 = editor.AddPoint(p1);
                        SlabShapeVertex v2 = editor.AddPoint(p2);
                        editor.ModifySubElement(v1, ridgeHeight);
                        editor.ModifySubElement(v2, ridgeHeight);
                        editor.AddSplitLine(v1, v2);
                    }
                    else if (chosenType == SlopeType.OneWay)
                    {
                        // === 单向坡 === (抬高一侧的长边，整体向另一侧长边排水)
                        double liftHeight = (isXLonger ? lengthY : lengthX) * slope;

                        // 找到需要抬高的两个角点
                        XYZ p1 = isXLonger ? new XYZ(min.X, max.Y, min.Z) : new XYZ(max.X, min.Y, min.Z);
                        XYZ p2 = isXLonger ? new XYZ(max.X, max.Y, min.Z) : new XYZ(max.X, max.Y, min.Z);

                        SlabShapeVertex v1 = editor.AddPoint(p1);
                        SlabShapeVertex v2 = editor.AddPoint(p2);
                        editor.ModifySubElement(v1, liftHeight);
                        editor.ModifySubElement(v2, liftHeight);
                        // 单向坡不需要额外画折线，只需抬高一侧即可
                    }

                    trans.Commit();
                }

                Autodesk.Revit.UI.TaskDialog.Show("成功", $"已成功生成 {chosenType}！");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}