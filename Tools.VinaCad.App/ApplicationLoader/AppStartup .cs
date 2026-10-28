using Microsoft.Win32;
using PrLogTrackingSystem;
using Teigha.Runtime;
using Tools.Resources.Language;

namespace Tools.VinaCad.App.ApplicationLoader
{
    public class AppStartup : IExtensionApplication
    {
        // Mã ngôn ngữ của VinaCAD (theo đội VinaCAD)
        private const int LANGUAGE_ENG = 1033;
        private const int LANGUAGE_JPN = 1041;
        private const int LANGUAGE_VIE = 1066;

        public void Initialize()
        {
            try
            {
                string code = GetVinaCadLanguageId() switch
                {
                    LANGUAGE_JPN => "ja-JP",
                    LANGUAGE_ENG => "en-US",
                    _ => ""   // tiếng Việt hoặc không xác định -> dùng Lang.resx
                };
                Translator.Instance.SetLanguage(code, save: false);
            }
            catch (System.Exception ex)
            {
                Logger.Info(nameof(Initialize), ex);
            }
        }

        public void Terminate() { }

        // VinaCAD lưu ngôn ngữ đang dùng tại:
        // HKEY_CURRENT_USER\Software\PrimaSolutions\VinaCAD Application\LanguageId
        private static int GetVinaCadLanguageId()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\PrimaSolutions\VinaCAD Application");
            return key?.GetValue("LanguageId") is int id ? id : LANGUAGE_VIE;
        }
    }
}