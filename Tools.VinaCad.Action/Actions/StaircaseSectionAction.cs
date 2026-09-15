using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Tools.View.UI;
using Tools.ViewModel;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public sealed class StaircaseSectionAction
    {
        private const string InsertionPointPrompt = "\nChọn điểm chèn mặt cắt cầu thang: ";
        private const string CompletionMessageFormat = "\nLTP: Đã tạo mặt cắt cầu thang gồm {0} đường.";
        private const string ErrorMessageFormat = "Lỗi LTP: {0}";

        public void Execute()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            try
            {
                //#if DEBUG
                //                StaircaseSectionGeometry.SelfCheck();
                //#endif
                double unitsPerMillimetre = StaircaseDrawingUnits.GetUnitsPerMillimetre();
                StaircaseSectionModel defaults = StaircaseSectionSetting.CreateDefault(unitsPerMillimetre);
                var settingsStore = new StaircaseSectionSettingsStore();
                StaircaseSectionModel settings = settingsStore.LoadOrDefault(unitsPerMillimetre, defaults);

                var dialogService = new StaircaseSectionDialogService(document.Editor);
                StaircaseSectionModel? configuredSettings = dialogService.Show(settings, defaults);
                if (configuredSettings == null) return;
                settings = configuredSettings;
                settingsStore.Save(settings, unitsPerMillimetre);

                PromptPointResult pointResult = document.Editor.GetPoint(InsertionPointPrompt);
                if (pointResult.Status != PromptStatus.OK) return;

                int lineCount = new StaircaseSectionDrawingService().Draw(
                    document.Database, pointResult.Value, settings);

                // B3: Cập nhật màn hình và trả kết quả cho người dùng.
                document.Editor.UpdateScreen();
                document.Editor.WriteMessage(string.Format(CompletionMessageFormat, lineCount));
            }
            catch (Exception exception)
            {
                Logger.Info(nameof(StaircaseSectionAction), exception);
                MessageBox.Show(
                    string.Format(ErrorMessageFormat, exception.Message),
                    StringDefinition.TITLE_ERROR);
            }
        }
    }

    internal sealed class StaircaseSectionDialogService
    {
        private const double MeasurementTolerance = 1e-9;
        private const string NumberFormatMessage = "Vui lòng nhập đúng định dạng số cho tất cả thông số.";
        private const string StoreyHeightPrompt = "\nChọn hai điểm đo chiều cao tầng: ";
        private const string Landing1Prompt = "\nChọn hai điểm đo chiều rộng chiếu nghỉ 1: ";
        private const string Landing2Prompt = "\nChọn hai điểm đo chiều rộng chiếu nghỉ 2: ";
        private const string DefaultMeasurementPrompt = "\nChọn hai điểm đo khoảng cách: ";

        private readonly Editor _editor;

        public StaircaseSectionDialogService(Editor editor)
        {
            _editor = editor;
        }

        public StaircaseSectionModel? Show(StaircaseSectionModel settings,StaircaseSectionModel defaults)
        {
            StaircaseSectionModel currentSettings = settings;

            while (true)
            {
                StaircaseSectionVM viewModel = CreateViewModel(currentSettings);
                StaircaseSectionWindow view = CreateView(viewModel, defaults);
                Application.ShowModalWindow(view);
                currentSettings = viewModel.Settings;

                if (view.DialogResult != true) return null;
                if (viewModel.IsAccepted) return currentSettings;

                TryApplyMeasurement(currentSettings, viewModel.MeasurementRequest);
            }
        }

        private static StaircaseSectionVM CreateViewModel(StaircaseSectionModel settings)
        {
            return new StaircaseSectionVM(settings);
        }

        private static StaircaseSectionWindow CreateView(
            StaircaseSectionVM viewModel,
            StaircaseSectionModel defaults)
        {
            var view = new StaircaseSectionWindow { DataContext = viewModel };

            viewModel.AcceptCmd = new RelayCommand(() => Accept(view, viewModel));
            viewModel.CancelCmd = new RelayCommand(() => view.DialogResult = false);
            viewModel.ResetCmd = new RelayCommand(() => viewModel.Reset(defaults.Copy()));

            viewModel.MeasureStoreyHeightCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.StoreyHeight);
            viewModel.MeasureLanding1WidthCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.Landing1Width);
            viewModel.MeasureLanding2WidthCmd = CreateMeasurementCommand(
                view, viewModel, StaircaseMeasurement.Landing2Width);
            return view;
        }

        private static RelayCommand CreateMeasurementCommand(
            Window view,
            StaircaseSectionVM viewModel,
            StaircaseMeasurement measurement)
        {
            return new RelayCommand(() => RequestMeasurement(view, viewModel, measurement));
        }

        private static void Accept(StaircaseSectionWindow view, StaircaseSectionVM viewModel)
        {
            if (view.HasValidationErrors())
            {
                MessageBox.Show(NumberFormatMessage, StringDefinition.TITLE_MESSAGE);
                return;
            }

            if (!viewModel.Settings.TryValidate(out string message))
            {
                MessageBox.Show(message, StringDefinition.TITLE_MESSAGE);
                return;
            }

            viewModel.IsAccepted = true;
            view.DialogResult = true;
        }

        private static void RequestMeasurement(
            Window view,
            StaircaseSectionVM viewModel,
            StaircaseMeasurement measurement)
        {
            viewModel.MeasurementRequest = measurement;
            view.DialogResult = true;
        }

        private void TryApplyMeasurement(
            StaircaseSectionModel settings,
            StaircaseMeasurement measurement)
        {
            var options = new PromptDistanceOptions(GetMeasurementPrompt(measurement));
            PromptDoubleResult result = _editor.GetDistance(options);
            if (result.Status != PromptStatus.OK || result.Value <= MeasurementTolerance) return;

            switch (measurement)
            {
                case StaircaseMeasurement.StoreyHeight:
                    settings.StoreyHeight = result.Value;
                    break;
                case StaircaseMeasurement.Landing1Width:
                    settings.Landing1Width = result.Value;
                    break;
                case StaircaseMeasurement.Landing2Width:
                    settings.Landing2Width = result.Value;
                    break;
            }
        }

        private static string GetMeasurementPrompt(StaircaseMeasurement measurement)
        {
            return measurement switch
            {
                StaircaseMeasurement.StoreyHeight => StoreyHeightPrompt,
                StaircaseMeasurement.Landing1Width => Landing1Prompt,
                StaircaseMeasurement.Landing2Width => Landing2Prompt,
                _ => DefaultMeasurementPrompt
            };
        }
    }

    internal sealed class StaircaseSectionDrawingService
    {
        private const string GroupDictionaryKey = "*A";
        private const string GroupDescription = "Mặt cắt cầu thang VinaCAD LTP";

        public int Draw(
            Database database,
            Point3d insertionPoint,
            StaircaseSectionModel settings)
        {
            // B1: Sinh và kiểm tra hình học trước khi mở transaction ghi.
            IReadOnlyList<StaircaseSegment> segments = StaircaseSectionGeometry.Generate(settings);

            // B2: Ghi toàn bộ đường và Group trong cùng một transaction nguyên tử.
            using Transaction transaction = database.TransactionManager.StartTransaction();
            BlockTableRecord modelSpace = OpenModelSpace(database, transaction);
            ObjectIdCollection entityIds = DrawSegments(
                database, transaction, modelSpace, insertionPoint, segments);

            if (settings.CreateGroup && entityIds.Count > 0)
            {
                CreateGroup(database, transaction, entityIds);
            }

            // B3: Commit và trả số đối tượng đã tạo.
            transaction.Commit();
            return entityIds.Count;
        }

        private static BlockTableRecord OpenModelSpace(
            Database database,
            Transaction transaction)
        {
            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        private static ObjectIdCollection DrawSegments(
            Database database,
            Transaction transaction,
            BlockTableRecord modelSpace,
            Point3d insertionPoint,
            IReadOnlyList<StaircaseSegment> segments)
        {
            var entityIds = new ObjectIdCollection();
            foreach (StaircaseSegment segment in segments)
            {
                Line line = CreateLine(database, insertionPoint, segment);
                modelSpace.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                entityIds.Add(line.ObjectId);
            }

            return entityIds;
        }

        private static Line CreateLine(
            Database database,
            Point3d insertionPoint,
            StaircaseSegment segment)
        {
            var line = new Line(
                ToPoint3d(insertionPoint, segment.Start),
                ToPoint3d(insertionPoint, segment.End));
            line.SetDatabaseDefaults(database);
            return line;
        }

        private static Point3d ToPoint3d(Point3d insertionPoint, StaircasePoint point)
        {
            return new Point3d(
                insertionPoint.X + point.X,
                insertionPoint.Y + point.Y,
                insertionPoint.Z);
        }

        private static void CreateGroup(
            Database database,
            Transaction transaction,
            ObjectIdCollection entityIds)
        {
            var groups = (DBDictionary)transaction.GetObject(
                database.GroupDictionaryId, OpenMode.ForWrite);
            var group = new Group(GroupDescription, true);
            groups.SetAt(GroupDictionaryKey, group);
            transaction.AddNewlyCreatedDBObject(group, true);
            group.Append(entityIds);
        }
    }
}
