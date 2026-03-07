using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitAddin
{
    // ==========================================
    // 1. 新增：线段选择过滤器，确保用户只能点选线条
    // ==========================================
    public class LineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is CurveElement;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class Test : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 🚨 需求3：拾取多条线段 (框选或按住Ctrl点选)
                IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new LineSelectionFilter(), "请在视图中拾取多条模型线或详图线 (选完点左上角完成)");
                if (refs.Count == 0) return Result.Cancelled;

                List<Curve> curves = new List<Curve>();
                foreach (Reference r in refs)
                {
                    CurveElement curveElem = doc.GetElement(r) as CurveElement;
                    if (curveElem != null)
                    {
                        curves.Add(curveElem.GeometryCurve);
                    }
                }

                // 🚨 将多条线段转化为连续的 CurveLoop (自动排序)
                CurveLoop curveLoop = ConnectCurves(curves);
                if (curveLoop == null) throw new Exception("选择的线段无法形成连续的路径！");

                Level level = doc.ActiveView.GenLevel;
                if (level == null)
                {
                    level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                }
                if (level == null) throw new Exception("当前项目中找不到任何标高，无法生成栏杆！");

                UltimateRailingWindow win = new UltimateRailingWindow(doc, curveLoop, level.Id);
                if (win.ShowDialog() == true)
                {
                    uidoc.RefreshActiveView();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("运行报错", ex.Message + "\n" + ex.StackTrace);
                return Result.Failed;
            }
        }

        // --- 辅助方法：连接多条乱序曲线 ---
        private CurveLoop ConnectCurves(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return null;

            CurveLoop loop = new CurveLoop();
            List<Curve> remaining = new List<Curve>(curves);

            Curve current = remaining[0];
            loop.Append(current);
            remaining.RemoveAt(0);

            XYZ currentEnd = current.GetEndPoint(1);

            while (remaining.Count > 0)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Curve next = remaining[i];
                    XYZ p0 = next.GetEndPoint(0);
                    XYZ p1 = next.GetEndPoint(1);

                    if (p0.IsAlmostEqualTo(currentEnd, 0.001))
                    {
                        loop.Append(next);
                        currentEnd = p1;
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                    else if (p1.IsAlmostEqualTo(currentEnd, 0.001))
                    {
                        Curve reversed = next.CreateReversed();
                        loop.Append(reversed);
                        currentEnd = reversed.GetEndPoint(1);
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                }
                // 允许硬接防止死循环
                if (!found)
                {
                    loop.Append(remaining[0]);
                    currentEnd = remaining[0].GetEndPoint(1);
                    remaining.RemoveAt(0);
                }
            }
            return loop;
        }
    }

    // ==========================================
    // 纯 C# WPF 终极控制面板 (线段生成版)
    // ==========================================
    public class UltimateRailingWindow : System.Windows.Window
    {
        private Document _doc;
        private CurveLoop _curveLoop;
        private ElementId _levelId;

        private System.Windows.Controls.TextBox txtTopHeight, txtTopW, txtTopH, txtTopT;
        private System.Windows.Controls.TextBox txtRailHeights, txtRailW, txtRailH, txtRailT;
        private System.Windows.Controls.TextBox txtPicketSpacing, txtPicketW, txtPicketH, txtPicketT;
        private System.Windows.Controls.TextBox txtMainPostSpacing, txtMainPostW, txtMainPostH, txtMainPostT;
        private System.Windows.Controls.TextBox txtStartW, txtStartH, txtStartT;
        private System.Windows.Controls.TextBox txtEndW, txtEndH, txtEndT;
        private System.Windows.Controls.TextBox txtCornerW, txtCornerH, txtCornerT;
        private System.Windows.Controls.CheckBox chkCorner;

        public UltimateRailingWindow(Document doc, CurveLoop curveLoop, ElementId levelId)
        {
            _doc = doc;
            _curveLoop = curveLoop;
            _levelId = levelId;

            this.Title = "一键多线段生成栏杆系统";
            this.Width = 650;
            this.Height = 700;
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            System.Windows.Controls.ScrollViewer scroll = new System.Windows.Controls.ScrollViewer();
            scroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;

            System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(15) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(180) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            int rowIndex = 0;

            Action<string> AddSectionTitle = (title) => {
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock { Text = title, FontWeight = System.Windows.FontWeights.Bold, Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new System.Windows.Thickness(0, 15, 0, 5) };
                System.Windows.Controls.Grid.SetRow(tb, rowIndex); System.Windows.Controls.Grid.SetColumnSpan(tb, 4); grid.Children.Add(tb);
                rowIndex++;
            };

            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            AddHeader(grid, rowIndex, "宽度(mm)", 1); AddHeader(grid, rowIndex, "高度(mm)", 2); AddHeader(grid, rowIndex, "厚度(mm)", 3);
            rowIndex++;

            AddSectionTitle("■ 1. 顶部扶手 (Top Rail)");
            AddInputRowSpacing(grid, ref rowIndex, "总高度(mm):", "900", out txtTopHeight);
            AddInputRow(grid, ref rowIndex, "截面尺寸:", "50", "50", "4", out txtTopW, out txtTopH, out txtTopT);

            AddSectionTitle("■ 2. 中间横杆 (Rail Structure)");
            AddInputRowSpacing(grid, ref rowIndex, "距地高度(逗号分隔):", "700, 100", out txtRailHeights);
            AddInputRow(grid, ref rowIndex, "截面尺寸:", "40", "20", "2", out txtRailW, out txtRailH, out txtRailT);

            AddSectionTitle("■ 3. 主阵列 (Baluster Pattern)");
            AddInputRowSpacing(grid, ref rowIndex, "悬空小竖杆间距(mm):", "150", out txtPicketSpacing);
            AddInputRow(grid, ref rowIndex, "小竖杆独立尺寸:", "20", "20", "2", out txtPicketW, out txtPicketH, out txtPicketT);

            AddInputRowSpacing(grid, ref rowIndex, "阵列大立柱间距(mm):", "1200", out txtMainPostSpacing);
            AddInputRow(grid, ref rowIndex, "大立柱独立尺寸:", "60", "60", "4", out txtMainPostW, out txtMainPostH, out txtMainPostT);

            AddSectionTitle("■ 4. 关键节点立柱");
            AddInputRow(grid, ref rowIndex, "起点立柱尺寸:", "60", "60", "4", out txtStartW, out txtStartH, out txtStartT);
            AddInputRow(grid, ref rowIndex, "终点立柱尺寸:", "60", "60", "4", out txtEndW, out txtEndH, out txtEndT);

            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            chkCorner = new System.Windows.Controls.CheckBox { Content = "启用转角立柱", IsChecked = true, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 10, 0, 5), FontWeight = System.Windows.FontWeights.Bold };
            System.Windows.Controls.Grid.SetRow(chkCorner, rowIndex); System.Windows.Controls.Grid.SetColumnSpan(chkCorner, 4); grid.Children.Add(chkCorner);
            rowIndex++;

            AddInputRow(grid, ref rowIndex, "转角立柱尺寸:", "60", "60", "4", out txtCornerW, out txtCornerH, out txtCornerT);
            chkCorner.Checked += (s, e) => { txtCornerW.IsEnabled = true; txtCornerH.IsEnabled = true; txtCornerT.IsEnabled = true; };
            chkCorner.Unchecked += (s, e) => { txtCornerW.IsEnabled = false; txtCornerH.IsEnabled = false; txtCornerT.IsEnabled = false; };

            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            System.Windows.Controls.Button btnApply = new System.Windows.Controls.Button
            {
                Content = "执行生成: 基于多线段创造新栏杆",
                Height = 45,
                Margin = new System.Windows.Thickness(0, 25, 0, 10),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D7")),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 15
            };
            btnApply.Click += BtnApply_Click;
            System.Windows.Controls.Grid.SetRow(btnApply, rowIndex); System.Windows.Controls.Grid.SetColumnSpan(btnApply, 4); grid.Children.Add(btnApply);

            scroll.Content = grid;
            this.Content = scroll;
        }

        private void AddHeader(System.Windows.Controls.Grid grid, int rowIndex, string text, int col)
        {
            System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock { Text = text, FontWeight = System.Windows.FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            System.Windows.Controls.Grid.SetRow(tb, rowIndex); System.Windows.Controls.Grid.SetColumn(tb, col); grid.Children.Add(tb);
        }

        private void AddInputRowSpacing(System.Windows.Controls.Grid grid, ref int rowIndex, string label, string defSpace, out System.Windows.Controls.TextBox sBox)
        {
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock { Text = label, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 5, 0, 5) };
            System.Windows.Controls.Grid.SetRow(tb, rowIndex); System.Windows.Controls.Grid.SetColumn(tb, 0); grid.Children.Add(tb);
            sBox = new System.Windows.Controls.TextBox { Text = defSpace, Margin = new System.Windows.Thickness(5), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetRow(sBox, rowIndex); System.Windows.Controls.Grid.SetColumn(sBox, 1); System.Windows.Controls.Grid.SetColumnSpan(sBox, 3); grid.Children.Add(sBox);
            rowIndex++;
        }

        private void AddInputRow(System.Windows.Controls.Grid grid, ref int rowIndex, string label, string defW, string defH, string defT, out System.Windows.Controls.TextBox wBox, out System.Windows.Controls.TextBox hBox, out System.Windows.Controls.TextBox tBox)
        {
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock { Text = label, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 5, 0, 5) };
            System.Windows.Controls.Grid.SetRow(tb, rowIndex); System.Windows.Controls.Grid.SetColumn(tb, 0); grid.Children.Add(tb);
            wBox = new System.Windows.Controls.TextBox { Text = defW, Margin = new System.Windows.Thickness(5), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetRow(wBox, rowIndex); System.Windows.Controls.Grid.SetColumn(wBox, 1); grid.Children.Add(wBox);
            hBox = new System.Windows.Controls.TextBox { Text = defH, Margin = new System.Windows.Thickness(5), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetRow(hBox, rowIndex); System.Windows.Controls.Grid.SetColumn(hBox, 2); grid.Children.Add(hBox);
            tBox = new System.Windows.Controls.TextBox { Text = defT, Margin = new System.Windows.Thickness(5), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetRow(tBox, rowIndex); System.Windows.Controls.Grid.SetColumn(tBox, 3); grid.Children.Add(tBox);
            rowIndex++;
        }

        private FamilySymbol GetOrCreateSym(Family baseFamily, string prefix, string wText, string hText, string tText)
        {
            string symName = $"{prefix}_{wText}x{hText}x{tText}mm";
            FamilySymbol sym = null;
            foreach (ElementId id in baseFamily.GetFamilySymbolIds())
            {
                FamilySymbol existingSym = _doc.GetElement(id) as FamilySymbol;
                if (existingSym != null && existingSym.Name == symName) { sym = existingSym; break; }
            }
            if (sym == null)
            {
                FamilySymbol baseSym = _doc.GetElement(baseFamily.GetFamilySymbolIds().First()) as FamilySymbol;
                sym = baseSym.Duplicate(symName) as FamilySymbol;

                Parameter pW = sym.LookupParameter("宽度"); Parameter pH = sym.LookupParameter("高度"); Parameter pT = sym.LookupParameter("厚度");
                if (pW != null && !pW.IsReadOnly) pW.Set(double.Parse(wText) / 304.8);
                if (pH != null && !pH.IsReadOnly) pH.Set(double.Parse(hText) / 304.8);
                if (pT != null && !pT.IsReadOnly) pT.Set(double.Parse(tText) / 304.8);
            }
            if (!sym.IsActive) sym.Activate();
            return sym;
        }

        private Family LoadFamilyIfNeeded(string path, string famName)
        {
            Family fam = new FilteredElementCollector(_doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == famName);
            if (fam == null)
            {
                if (!System.IO.File.Exists(path)) throw new Exception("找不到族文件: " + path);
                _doc.LoadFamily(path, out fam);
            }
            return fam;
        }

        // ==========================================
        // 核心生成逻辑: 彻底去掉冗余赋值代码
        // ==========================================
        private void BtnApply_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "基于多线段生成定制栏杆"))
                {
                    t.Start();

                    Family profileFam = LoadFamilyIfNeeded(@"E:\C#-Revit\test1-master\lh7986815-cmd\test1\test1\截面.rfa", "截面");
                    Family postFam = LoadFamilyIfNeeded(@"E:\C#-Revit\test1-master\lh7986815-cmd\test1\test1\中间立柱.rfa", "中间立柱");

                    FamilySymbol topRailSym = GetOrCreateSym(profileFam, "顶部", txtTopW.Text, txtTopH.Text, txtTopT.Text);
                    FamilySymbol horizRailSym = GetOrCreateSym(profileFam, "横杆", txtRailW.Text, txtRailH.Text, txtRailT.Text);
                    FamilySymbol picketSym = GetOrCreateSym(postFam, "小竖杆", txtPicketW.Text, txtPicketH.Text, txtPicketT.Text);
                    FamilySymbol mainPostSym = GetOrCreateSym(postFam, "阵列大立柱", txtMainPostW.Text, txtMainPostH.Text, txtMainPostT.Text);
                    FamilySymbol startSym = GetOrCreateSym(postFam, "起点", txtStartW.Text, txtStartH.Text, txtStartT.Text);
                    FamilySymbol endSym = GetOrCreateSym(postFam, "终点", txtEndW.Text, txtEndH.Text, txtEndT.Text);
                    FamilySymbol cornerSym = chkCorner.IsChecked == true ? GetOrCreateSym(postFam, "转角", txtCornerW.Text, txtCornerH.Text, txtCornerT.Text) : null;

                    // 🚨 务必确信你在项目中将用作母体的那个自带栏杆的对齐设为了“展开样式以匹配”
                    RailingType baseType = new FilteredElementCollector(_doc)
                        .OfClass(typeof(RailingType))
                        .Cast<RailingType>()
                        .FirstOrDefault(rt => rt.BalusterPlacement.BalusterPattern.GetBalusterCount() > 0);

                    if (baseType == null) throw new Exception("未找到有竖杆设置的栏杆类型作为母体！");

                    RailingType newType = baseType.Duplicate("全栈定制_" + Guid.NewGuid().ToString().Substring(0, 4)) as RailingType;

                    ElementId topRailId = newType.TopRailType;
                    if (topRailId != ElementId.InvalidElementId)
                    {
                        TopRailType oldTop = _doc.GetElement(topRailId) as TopRailType;
                        TopRailType newTop = oldTop.Duplicate("定制顶部_" + Guid.NewGuid().ToString().Substring(0, 4)) as TopRailType;
                        newType.TopRailType = newTop.Id;
                        newTop.ProfileId = topRailSym.Id;
                    }

                    Parameter pTopHeight = newType.get_Parameter(BuiltInParameter.RAILING_SYSTEM_TOP_RAIL_HEIGHT_PARAM);
                    if (pTopHeight != null && !pTopHeight.IsReadOnly) pTopHeight.Set(double.Parse(txtTopHeight.Text) / 304.8);

                    // --- 横杆处理 (实时代理，只改不设) ---
                    NonContinuousRailStructure rails = newType.RailStructure;
                    while (rails.GetNonContinuousRailCount() > 0)
                    {
                        rails.RemoveNonContinuousRail(0);
                    }

                    string[] heightStrs = txtRailHeights.Text.Split(new char[] { ',', '，' });
                    List<double> parsedHeights = heightStrs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => double.Parse(s.Trim())).OrderByDescending(h => h).ToList();

                    for (int i = 0; i < parsedHeights.Count; i++)
                    {
                        NonContinuousRailInfo newRail = rails.AddNonContinuousRail($"横杆 {i + 1}", parsedHeights[i] / 304.8, 0.0);
                        newRail.ProfileId = horizRailSym.Id;
                    }

                    // --- 主阵列处理 (实时代理，只改不设) ---
                    BalusterPlacement bp = newType.BalusterPlacement;
                    BalusterPattern pat = bp.BalusterPattern;

                    pat.DistributionJustification = PatternJustification.SpreadPatternToFit;

                    // 越界保险：如果没有行，先造一行出来
                    if (pat.GetBalusterCount() == 0) pat.DuplicateBaluster(0);
                    while (pat.GetBalusterCount() > 1) pat.RemoveBaluster(pat.GetBalusterCount() - 1);

                    double pSpace = double.Parse(txtPicketSpacing.Text);
                    double mSpace = double.Parse(txtMainPostSpacing.Text);
                    int totalRows = (int)Math.Round(mSpace / pSpace);
                    if (totalRows < 1) totalRows = 1;

                    BalusterInfo bInfo0 = pat.GetBaluster(0);
                    string picketTopRef = parsedHeights.Count > 0 ? "横杆 1" : "顶部扶栏图元";
                    string picketBaseRef = parsedHeights.Count > 0 ? $"横杆 {parsedHeights.Count}" : "主体";

                    if (totalRows == 1)
                    {
                        bInfo0.BalusterFamilyId = picketSym.Id;
                        bInfo0.TopReferenceName = picketTopRef;
                        bInfo0.BaseReferenceName = picketBaseRef;
                        bInfo0.DistanceFromPreviousOrSpace = pSpace / 304.8;
                        bInfo0.TopOffset = 0.0; bInfo0.BaseOffset = 0.0;
                    }
                    else
                    {
                        // 大柱基因
                        bInfo0.BalusterFamilyId = mainPostSym.Id;
                        bInfo0.TopReferenceName = "顶部扶栏图元";
                        bInfo0.BaseReferenceName = "主体";
                        bInfo0.DistanceFromPreviousOrSpace = pSpace / 304.8;
                        bInfo0.TopOffset = 0.0; bInfo0.BaseOffset = 0.0;

                        pat.DuplicateBaluster(0);

                        // 小杆基因
                        bInfo0 = pat.GetBaluster(0);
                        bInfo0.BalusterFamilyId = picketSym.Id;
                        bInfo0.TopReferenceName = picketTopRef;
                        bInfo0.BaseReferenceName = picketBaseRef;

                        for (int i = 0; i < totalRows - 2; i++)
                        {
                            pat.DuplicateBaluster(0);
                        }
                    }

                    // --- 节点立柱处理 ---
                    PostPattern postPat = bp.PostPattern;

                    if (postPat.StartPost != null)
                    {
                        postPat.StartPost.BalusterFamilyId = startSym.Id;
                        postPat.StartPost.TopReferenceName = "顶部扶栏图元";
                        postPat.StartPost.BaseReferenceName = "主体";
                        postPat.StartPost.DistanceFromPreviousOrSpace = (double.Parse(txtStartW.Text) / 2.0) / 304.8;
                    }
                    if (postPat.EndPost != null)
                    {
                        postPat.EndPost.BalusterFamilyId = endSym.Id;
                        postPat.EndPost.TopReferenceName = "顶部扶栏图元";
                        postPat.EndPost.BaseReferenceName = "主体";
                        postPat.EndPost.DistanceFromPreviousOrSpace = -(double.Parse(txtEndW.Text) / 2.0) / 304.8;
                    }

                    if (chkCorner.IsChecked == true && postPat.CornerPost != null)
                    {
                        postPat.CornerPost.BalusterFamilyId = cornerSym.Id;
                        postPat.CornerPost.TopReferenceName = "顶部扶栏图元";
                        postPat.CornerPost.BaseReferenceName = "主体";
                        postPat.CornerPost.DistanceFromPreviousOrSpace = 0.0;
                    }
                    else if (postPat.CornerPost != null)
                    {
                        postPat.CornerPost.BalusterFamilyId = ElementId.InvalidElementId;
                    }

                    // 直接生成！不加多余的废话
                    Railing.Create(_doc, _curveLoop, newType.Id, _levelId);

                    _doc.Regenerate();
                    t.Commit();
                }

                this.DialogResult = true;
                this.Close();
                Autodesk.Revit.UI.TaskDialog.Show("生成成功", "多段折线生成完成！");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace, "发生错误");
            }
        }
    }
}