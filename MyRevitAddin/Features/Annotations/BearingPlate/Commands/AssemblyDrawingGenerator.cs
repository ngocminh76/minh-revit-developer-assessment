using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MyRevitAddin.Features.Annotations.BearingPlate.Models;

namespace MyRevitAddin.Features.Annotations.BearingPlate.Commands
{
    public enum AnchorType
    {
        TopRight,
        TopCenter,
        BelowPrevious,
        TopLeft
    }

    public class AssemblyDrawingGenerator
    {
        private Document _doc;
        private ElementId _titleblockA3Id;
        private ElementId _titleblockA4Id;
        private ElementId _template3DId;
        private ElementId _templatePlanId;
        private ElementId _templateFrontId;
        private List<ViewSchedule> _scheduleTemplates;

        public AssemblyDrawingGenerator(Document doc)
        {
            _doc = doc;
        }

        public void GenerateDrawings(IEnumerable<BearingPlateModel> selectedPlates, Action<int, int, string> progressCallback = null)
        {
            var platesList = selectedPlates.ToList();
            int total = platesList.Count;
            if (total == 0) return;

            using (TransactionGroup tg = new TransactionGroup(_doc, "Generate Bearing Plate Drawings"))
            {
                tg.Start();

                InitializeTemplates();

                HashSet<ElementId> processedAssemblyTypes = new HashSet<ElementId>();
                int count = 0;

                foreach (var plate in platesList)
                {
                    count++;
                    progressCallback?.Invoke(count, total, $"Processing {plate.Type}...");
                    
                    Element element = _doc.GetElement(new ElementId(plate.ElementId));
                    if (element == null) continue;

                    AssemblyInstance assembly = GetOrCreateAssembly(element);
                    if (assembly == null) continue;

                    using (Transaction t2 = new Transaction(_doc, "Generate Views"))
                    {
                        t2.Start();
                        
                        string desiredName = plate.Type;
                        if (assembly.AssemblyTypeName != desiredName)
                        {
                            try { assembly.AssemblyTypeName = desiredName; } catch { } 
                        }

                        ElementId assemblyTypeId = assembly.GetTypeId();

                        // GROUPING LOGIC: Skip if we already generated drawings for this Assembly Type
                        if (processedAssemblyTypes.Contains(assemblyTypeId))
                        {
                            t2.RollBack();
                            continue;
                        }
                        
                        processedAssemblyTypes.Add(assemblyTypeId);
                        string asmName = assembly.AssemblyTypeName;
                        progressCallback?.Invoke(count, total, $"Generating Drawings for {asmName}...");

                        // OVERWRITE LOGIC
                        DeleteOldAssemblyViews(assemblyTypeId);

                        // Determine TitleBlock
                        bool useA4 = ShouldUseA4(element);
                        ElementId selectedTitleblockId = useA4 ? _titleblockA4Id : _titleblockA3Id;
                        if (selectedTitleblockId == ElementId.InvalidElementId) 
                            selectedTitleblockId = _titleblockA3Id;

                        // Create Sheet
                        ViewSheet sheet = CreateSheet(assembly, asmName, selectedTitleblockId);

                        // Set TitleBlock Parameters
                        if (sheet != null)
                        {
                            _doc.Regenerate();
                            FamilyInstance titleBlockInst = new FilteredElementCollector(_doc, sheet.Id)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .OfClass(typeof(FamilyInstance))
                                .Cast<FamilyInstance>()
                                .FirstOrDefault();

                            if (titleBlockInst != null)
                            {
                                try 
                                { 
                                    Parameter compCountParam = titleBlockInst.LookupParameter("Component Type Count");
                                    if (compCountParam != null && !compCountParam.IsReadOnly)
                                    {
                                        if (compCountParam.StorageType == StorageType.String) compCountParam.Set("1");
                                        else if (compCountParam.StorageType == StorageType.Integer) compCountParam.Set(1);
                                        else if (compCountParam.StorageType == StorageType.Double) compCountParam.Set(1.0);
                                    }
                                } 
                                catch { }

                                try 
                                { 
                                    Parameter countParam = titleBlockInst.LookupParameter("Count");
                                    if (countParam != null && !countParam.IsReadOnly)
                                    {
                                        if (countParam.StorageType == StorageType.String) countParam.Set("0");
                                        else if (countParam.StorageType == StorageType.Integer) countParam.Set(0);
                                        else if (countParam.StorageType == StorageType.Double) countParam.Set(0.0);
                                    }
                                } 
                                catch { }
                            }
                        }

                        // Create Views
                        var (view3d, planView, frontView) = CreateAssemblyViews(assembly);

                        // Create Schedules
                        List<ViewSchedule> schedules = CreateAssemblySchedules(assembly);

                        // Layout Views on Sheet
                        if (sheet != null)
                        {
                            Viewport vpFront = frontView != null ? Viewport.Create(_doc, sheet.Id, frontView.Id, new XYZ(0,0,0)) : null;
                            Viewport vpPlan = planView != null ? Viewport.Create(_doc, sheet.Id, planView.Id, new XYZ(0,0,0)) : null;
                            Viewport vp3d = view3d != null ? Viewport.Create(_doc, sheet.Id, view3d.Id, new XYZ(0,0,0)) : null;

                            LayoutViewsOnSheet(sheet, vpFront, vpPlan, vp3d, schedules, useA4);
                        }

                        t2.Commit();
                    }
                }

                tg.Assimilate();
            }
        }

        private bool ShouldUseA4(Element element)
        {
            try
            {
                ElementType type = _doc.GetElement(element.GetTypeId()) as ElementType;
                if (type != null)
                {
                    Parameter typeCommentsParam = type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS) ?? type.LookupParameter("Type Comments");
                    string typeComments = typeCommentsParam?.AsString();
                    if (!string.IsNullOrEmpty(typeComments))
                    {
                        string dimsPart = typeComments.Split('-')[0].Trim();
                        var parts = dimsPart.Split('x', 'X', '*');
                        double maxDim = 0;
                        foreach (var part in parts)
                        {
                            if (double.TryParse(part.Trim(), out double val))
                            {
                                if (val > maxDim) maxDim = val;
                            }
                        }
                        return maxDim > 0 && maxDim <= 320;
                    }
                }
            }
            catch { }
            return false;
        }

        private void InitializeTemplates()
        {
            var titleblocks = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            _titleblockA3Id = titleblocks.FirstOrDefault(x => x.Name == "REVGEN_CRH TitleBlock Tegningshoved - Bestillingsvarer A3 - 297x420 - Landscape")?.Id 
                              ?? titleblocks.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            
            _titleblockA4Id = titleblocks.FirstOrDefault(x => x.Name == "REVGEN_CRH TitleBlock Tegningshoved - Bestillingsvarer A4 - 297x210 - Portrait")?.Id 
                              ?? titleblocks.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;

            var viewTemplates = new FilteredElementCollector(_doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate && v.Name.StartsWith("REVGEN_CRH_BearingPlate"))
                .ToList();

            _template3DId = viewTemplates.FirstOrDefault(v => v.Name == "REVGEN_CRH_BearingPlate 3D")?.Id ?? ElementId.InvalidElementId;
            _templatePlanId = viewTemplates.FirstOrDefault(v => v.Name == "REVGEN_CRH_BearingPlate Plan")?.Id ?? ElementId.InvalidElementId;
            _templateFrontId = viewTemplates.FirstOrDefault(v => v.Name == "REVGEN_CRH_BearingPlate Opstalt")?.Id ?? ElementId.InvalidElementId;

            _scheduleTemplates = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(x => x.IsTemplate && x.Name.StartsWith("REVGEN_CRH"))
                .OrderBy(x => x.Name)
                .ToList();
        }

        private AssemblyInstance GetOrCreateAssembly(Element element)
        {
            if (element.AssemblyInstanceId == ElementId.InvalidElementId)
            {
                using (Transaction t1 = new Transaction(_doc, "Create Assembly"))
                {
                    t1.Start();
                    ElementId categoryId = element.Category.Id;
                    AssemblyInstance assembly = AssemblyInstance.Create(_doc, new List<ElementId> { element.Id }, categoryId);
                    t1.Commit(); 
                    return assembly;
                }
            }
            return _doc.GetElement(element.AssemblyInstanceId) as AssemblyInstance;
        }

        private void DeleteOldAssemblyViews(ElementId assemblyTypeId)
        {
            var instancesOfThisType = new FilteredElementCollector(_doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .Where(a => a.GetTypeId() == assemblyTypeId)
                .Select(a => a.Id)
                .ToList();

            var oldViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => instancesOfThisType.Contains(v.AssociatedAssemblyInstanceId) && !v.IsTemplate)
                .Select(v => v.Id)
                .ToList();

            foreach (var viewId in oldViews)
            {
                try { _doc.Delete(viewId); } catch { }
            }
        }

        private ViewSheet CreateSheet(AssemblyInstance assembly, string asmName, ElementId titleblockId)
        {
            ViewSheet sheet = AssemblyViewUtils.CreateSheet(_doc, assembly.Id, titleblockId);
            if (sheet != null)
            {
                try { sheet.Name = "Bearing Plate " + asmName; } catch { }
                try { sheet.SheetNumber = asmName + "-PL"; } catch { }
            }
            return sheet;
        }

        private (View3D, ViewSection, ViewSection) CreateAssemblyViews(AssemblyInstance assembly)
        {
            View3D view3d = AssemblyViewUtils.Create3DOrthographic(_doc, assembly.Id);
            ViewSection planView = AssemblyViewUtils.CreateDetailSection(_doc, assembly.Id, AssemblyDetailViewOrientation.HorizontalDetail);
            ViewSection frontView = AssemblyViewUtils.CreateDetailSection(_doc, assembly.Id, AssemblyDetailViewOrientation.ElevationFront);

            if (view3d != null) 
            { 
                try { view3d.Name = "3D Ortho"; } catch { } 
                if (_template3DId != ElementId.InvalidElementId) view3d.ViewTemplateId = _template3DId;
            }
            if (planView != null) 
            { 
                try { planView.Name = "Plan"; } catch { } 
                if (_templatePlanId != ElementId.InvalidElementId) planView.ViewTemplateId = _templatePlanId;
            }
            if (frontView != null) 
            { 
                try { frontView.Name = "Front"; } catch { } 
                if (_templateFrontId != ElementId.InvalidElementId) frontView.ViewTemplateId = _templateFrontId;
            }

            return (view3d, planView, frontView);
        }

        private List<ViewSchedule> CreateAssemblySchedules(AssemblyInstance assembly)
        {
            List<ViewSchedule> schedules = new List<ViewSchedule>();
            string logPath = @"D:\03.MINH\REVIT\RevitTest\schedule_log.txt";
            System.IO.File.AppendAllText(logPath, $"\n--- Creating schedules for Assembly: {assembly.Name} ---\n");

            foreach (var template in _scheduleTemplates)
            {
                // Lấy đúng Category của Template để tạo Schedule (tránh mất cột/field do sai Category)
                ElementId catId = template.Definition.CategoryId;
                bool isMaterialTakeoff = false;
                try { isMaterialTakeoff = template.Definition.IsMaterialTakeoff; } catch { }

                System.IO.File.AppendAllText(logPath, $"Template: {template.Name}, CatId: {catId}, IsMaterialTakeoff: {isMaterialTakeoff}\n");

                if (catId == ElementId.InvalidElementId) 
                    catId = new ElementId((long)BuiltInCategory.OST_GenericModel);

                ViewSchedule schedule = null;
                try
                {
                    if (isMaterialTakeoff)
                    {
                        // Hàm CreateMaterialTakeoff trong Revit API yêu cầu tham số 3 là viewTemplateId chứ không phải categoryId
                        schedule = AssemblyViewUtils.CreateMaterialTakeoff(_doc, assembly.Id, template.Id, true);
                    }
                    else
                    {
                        schedule = AssemblyViewUtils.CreateSingleCategorySchedule(_doc, assembly.Id, catId);
                    }
                }
                catch (System.Exception ex)
                {
                    System.IO.File.AppendAllText(logPath, $"  FAILED to create exact schedule: {ex.Message}\n");
                }

                if (schedule != null)
                {
                    schedule.ViewTemplateId = template.Id;
                    
                    // COPY COLUMN WIDTHS TO PREVENT TEXT WRAPPING AND MISMATCHED HEIGHT (Max BoundingBox)
                    try
                    {
                        Autodesk.Revit.DB.TableData templateTD = template.GetTableData();
                        Autodesk.Revit.DB.TableData targetTD = schedule.GetTableData();
                        Autodesk.Revit.DB.TableSectionData templateBody = templateTD.GetSectionData(Autodesk.Revit.DB.SectionType.Body);
                        Autodesk.Revit.DB.TableSectionData targetBody = targetTD.GetSectionData(Autodesk.Revit.DB.SectionType.Body);
                        if (templateBody != null && targetBody != null)
                        {
                            int cols = Math.Min(templateBody.NumberOfColumns, targetBody.NumberOfColumns);
                            for (int i = 0; i < cols; i++)
                            {
                                targetBody.SetColumnWidth(i, templateBody.GetColumnWidth(i));
                            }
                        }
                    }
                    catch { }

                    try 
                    { 
                        string newName = template.Name;
                        if (newName.Contains("Template"))
                            newName = newName.Replace("Template", "Assembly");
                        else if (!newName.EndsWith("- Assembly"))
                            newName += " - Assembly";
                            
                        schedule.Name = newName; 
                    } 
                    catch { }
                    schedules.Add(schedule);
                }
            }
            return schedules;
        }

        // ==========================================
        // DYNAMIC LAYOUT SYSTEM
        // ==========================================

        private void LayoutViewsOnSheet(ViewSheet sheet, Viewport vpFront, Viewport vpPlan, Viewport vp3d, List<ViewSchedule> schedules, bool isA4)
        {
            // 3. Layout Schedules
            double mmToFeet = 1.0 / 304.8;
            
            // Lấy BoundingBox thực tế của Khung tên
            BoundingBoxXYZ tbBox = GetTitleBlockBoundingBox(sheet);
            if (tbBox == null) return;

            // Đọc tọa độ Min thực tế của Khung tên (A4 là 0, A3 là -0.69 feet)
            double paperMinX = tbBox.Min.X;
            double paperMinY = tbBox.Min.Y;

            // Danh sách tọa độ chính xác tuyệt đối (Sheet Space) theo ảnh của user (đơn vị feet)
            // Tọa độ này đúng cho CẢ A3 và A4 vì Khung tên A3 và A4 có chung một hệ quy chiếu gốc (từ bên phải)
            var schedulePositions = new System.Collections.Generic.Dictionary<string, XYZ>
            {
                { "Base Component", new XYZ(0.01, 0.23, 0) },
                { "Weight", new XYZ(0.21, 0.24, 0) },
                { "Corrosion Category", new XYZ(0.13, 0.18, 0) },
                { "Surface Treatment", new XYZ(0.13, 0.16, 0) },
                { "Description", new XYZ(0.36, 0.01, 0) },
                { "Additional Components", new XYZ(0.01, 0.22, 0) }
            };

            double currentOtherY = paperMinY + 150.0 * mmToFeet;

            foreach (var schedule in schedules)
            {
                // Place schedule temporarily at origin
                var ssi = ScheduleSheetInstance.Create(_doc, sheet.Id, schedule.Id, new XYZ(0, 0, 0));
                _doc.Regenerate();
                
                BoundingBoxXYZ bbox = ssi.get_BoundingBox(sheet);
                if (bbox != null)
                {
                    XYZ currentMin = bbox.Min;
                    XYZ targetMin = null;

                    // Khớp tên Schedule để lấy tọa độ tuyệt đối tương ứng
                    foreach (var kvp in schedulePositions)
                    {
                        if (schedule.Name.Contains(kvp.Key))
                        {
                            targetMin = kvp.Value; // Dùng TỌA ĐỘ TUYỆT ĐỐI
                            break;
                        }
                    }

                    if (targetMin != null)
                    {
                        ElementTransformUtils.MoveElement(_doc, ssi.Id, targetMin - currentMin);
                    }
                    else
                    {
                        // Tạm thời xếp các bảng khác lên cao để chờ user cung cấp thêm tọa độ
                        targetMin = new XYZ(0.01, currentOtherY, 0);
                        ElementTransformUtils.MoveElement(_doc, ssi.Id, targetMin - currentMin);
                        
                        double height = bbox.Max.Y - bbox.Min.Y;
                        currentOtherY += height + (5.0 * mmToFeet); 
                    }
                }
            }

            // 2. 3D View
            if (vp3d != null)
            {
                AlignViewport(sheet, vp3d, tbBox, AnchorType.TopRight, padding: 0.0);
            }

            // 3. Front View
            if (vpFront != null)
            {
                AlignViewport(sheet, vpFront, tbBox, AnchorType.TopCenter, padding: 0.05);
            }

            // 4. Plan View
            if (vpPlan != null)
            {
                _doc.Regenerate();
                BoundingBoxXYZ fBox = vpFront?.get_BoundingBox(sheet);
                AlignViewport(sheet, vpPlan, tbBox, AnchorType.BelowPrevious, fBox, padding: 0.05);
            }
        }

        private BoundingBoxXYZ GetTitleBlockBoundingBox(ViewSheet sheet)
        {
            FamilyInstance titleBlockInst = new FilteredElementCollector(_doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            return titleBlockInst?.get_BoundingBox(sheet);
        }

        private void GetDrawingArea(BoundingBoxXYZ tbBox, out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = 0.05; maxX = 1.0;
            minY = 0.05; maxY = 0.85;

            if (tbBox != null)
            {
                double tbWidth = tbBox.Max.X - tbBox.Min.X;
                double tbHeight = tbBox.Max.Y - tbBox.Min.Y;
                minX = tbBox.Min.X + 0.05 * tbWidth;
                maxX = tbBox.Max.X - 0.20 * tbWidth; 
                minY = tbBox.Min.Y + 0.05 * tbHeight;
                maxY = tbBox.Max.Y - 0.05 * tbHeight;
            }
        }

        private void AlignViewport(ViewSheet sheet, Viewport vp, BoundingBoxXYZ tbBox, AnchorType anchor, BoundingBoxXYZ previousBox = null, double padding = 0.05)
        {
            if (vp == null) return;

            _doc.Regenerate();
            BoundingBoxXYZ vpBox = vp.get_BoundingBox(sheet);
            if (vpBox == null) return;

            double width = vpBox.Max.X - vpBox.Min.X;
            double height = vpBox.Max.Y - vpBox.Min.Y;

            GetDrawingArea(tbBox, out double drawMinX, out double drawMaxX, out double drawMinY, out double drawMaxY);
            double drawCenterX = (drawMinX + drawMaxX) / 2;

            double targetX = 0;
            double targetY = 0;

            switch (anchor)
            {
                case AnchorType.TopRight:
                    targetX = (tbBox != null ? tbBox.Max.X : drawMaxX) - padding - (width / 2);
                    targetY = (tbBox != null ? tbBox.Max.Y : drawMaxY) - padding - (height / 2);
                    break;

                case AnchorType.TopCenter:
                    targetX = drawCenterX;
                    targetY = drawMaxY - padding - (height / 2);
                    break;

                case AnchorType.BelowPrevious:
                    targetX = drawCenterX; 
                    targetY = drawMinY + 0.3; // Default fallback
                    if (previousBox != null)
                    {
                        targetY = previousBox.Min.Y - padding - (height / 2);
                    }
                    break;
                case AnchorType.TopLeft:
                    targetX = drawMinX + (width / 2);
                    targetY = drawMaxY - padding - (height / 2);
                    break;
            }

            XYZ currentCenter = vp.GetBoxCenter();
            XYZ targetCenter = new XYZ(targetX, targetY, 0);
            ElementTransformUtils.MoveElement(_doc, vp.Id, targetCenter - currentCenter);
        }
    }
}
