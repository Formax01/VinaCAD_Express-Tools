//using Prima.VinaCAD.ApplicationServices;
//using Teigha.Runtime;
//using Tools.Resources.Definitions;
//using PrLogTrackingSystem;
//using PrMVVMCore;
//using System;
//using System.IO;
//using System.Reflection;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using AcadApp = Prima.VinaCAD.ApplicationServices.Application;
//using Exception = System.Exception;

//namespace Tools.AutoCad.App.ApplicationLoader
//{
//    public class ApplicationLoader : IExtensionApplication
//    {
//        public void Initialize()
//        {
//            try
//            {
//                CreateRibbon();
//                Document doc = AcadApp.DocumentManager.MdiActiveDocument;
//                if (doc != null && doc.Database != null)
//                {
//                    DocumentManager.Instance.RegisterCommandEvent(doc);
//                }
//            }
//            catch (System.Exception ex)
//            {
//                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
//                Logger.Info(nameof(Initialize), ex);
//            }
//        }
            
//        public void CreateRibbon()
//        {
//            try
//            {
//                RibbonControl ribbon = ComponentManager.Ribbon;
//                if (ribbon != null)
//                {
//                    RibbonTab rtab = ribbon.FindTab(StringDefinition.RIBBON_ID);
//                    if (rtab != null)
//                    {
//                        ribbon.Tabs.Remove(rtab);
//                    }

//                    rtab = new RibbonTab();
//                    rtab.Title = StringDefinition.RIBBON_TITLE;
//                    rtab.Id = StringDefinition.RIBBON_ID;
//                    ribbon.Tabs.Insert(ribbon.Tabs.Count, rtab);
//                    AddContentToTab(rtab);
//                    rtab.IsActive = true;
//                }
//            }
//            catch (Exception ex)
//            {
//                Logger.Info(nameof(CreateRibbon), ex);
//                throw new Exception(ex.Message, ex);
//            }
            
//        }

//        private void AddContentToTab(RibbonTab rtab)
//        {
//            try
//            {
//                rtab.Panels.Add(AddPanelSetting());
//            }
//            catch (Exception ex)
//            {
//                Logger.Info(nameof(AddContentToTab), ex);
//                throw new Exception(ex.Message, ex);
//            }
//        }
//        private RibbonPanel AddPanelSetting()
//        {
//            try
//            {
//                RibbonPanelSource rps = new RibbonPanelSource();
//                rps.Title = StringDefinition.RIBBON_PANEL_SETTING;
//                RibbonPanel rp = new RibbonPanel();
//                rp.Source = rps;

//                //create button Setting
//                var addinAssembly = typeof(ApplicationLoader).Assembly;
//                RibbonButton btnSetting = new RibbonButton
//                {
//                    Orientation = Orientation.Vertical,
//                    AllowInStatusBar = true,
//                    Size = RibbonItemSize.Large,
//                    Name = "Setting",
//                    ShowText = true,
//                    Text = "Setting",
//                    Description = "Setting",
//                    Image = GetEmbeddedPng(addinAssembly, "Setting.png"),
//                    LargeImage = GetEmbeddedPng(addinAssembly, "Setting.png"),
//                    CommandHandler = new RelayCommand(new Commands.Commands().BasicSettingsCommand)
//                };
//                rps.Items.Add(btnSetting);

//                return rp;
//            }
//            catch (Exception ex)
//            {
//                Logger.Info(nameof(AddPanelSetting), ex);
//                throw new Exception(ex.Message, ex);
//            }
//        }
//        public static ImageSource GetEmbeddedPng(System.Reflection.Assembly app, string imageName)
//        {
//            try
//            {
//                string assemblyDirectoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
//                string contentsFolder = Path.GetDirectoryName(assemblyDirectoryPath);
//                string resourcesFolder = Path.Combine(contentsFolder, "Resources");
//                string imagesFolder = Path.Combine(resourcesFolder, "Images");
//                var iconUri = new Uri(Path.Combine(imagesFolder, imageName), UriKind.Absolute);
//                var bitmapImage = new BitmapImage(iconUri);
//                return bitmapImage;
//            }
//            catch (Exception ex)
//            {
//                Logger.Info(nameof(GetEmbeddedPng), ex);
//                throw new Exception(ex.Message, ex);
//            }
//        }

//        public void Terminate()
//        {
            
//        }
//    }
//}
