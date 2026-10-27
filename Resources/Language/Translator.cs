using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tools.Resources.Language
{
    public sealed class Translator : INotifyPropertyChanged
    {
        public static Translator Instance { get; } = new Translator();

        // Danh sách ngôn ngữ hỗ trợ. Code "" = ngôn ngữ mặc định (Lang.resx).
        // Thêm ngôn ngữ mới: tạo file Lang.xx-XX.resx rồi thêm 1 dòng ở đây.
        public static readonly IReadOnlyList<LanguageInfo> Supported = new[]
        {
            new LanguageInfo("",      "Vietnamese", "Tiếng Việt"),
            new LanguageInfo("ja-JP", "Japanese",   "日本語"),
            new LanguageInfo("en-US", "English",    "English"),
        };

        // File lưu lựa chọn: %AppData%\VinaCAD_ExpressTools\language.txt
        private static readonly string SettingFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VinaCAD_ExpressTools", "language.txt");

        private Translator() { }

        public string CurrentCode { get; private set; } = "";

        public LanguageInfo Current => Supported.First(l => l.Code == CurrentCode);

        // Dùng cho binding XAML: {Binding [TenKey], Source={x:Static lang:Translator.Instance}}
        public string this[string key] =>
            Lang.ResourceManager.GetString(key, Lang.Culture) ?? key;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Dùng cho chỗ không binding được: tiêu đề palette, chuỗi trong ViewModel...
        public event EventHandler? LanguageChanged;

        // Gọi 1 lần lúc plugin khởi động (trong AppStartup.Initialize)
        public void Initialize()
        {
            SetLanguage(LoadSavedCode(), save: false);
        }

        public void SetLanguage(string? code, bool save = true)
        {
            code = code ?? "";
            if (!Supported.Any(l => l.Code == code)) code = "";

            CurrentCode = code;
            Lang.Culture = code == ""
                ? CultureInfo.InvariantCulture   // dùng Lang.resx
                : new CultureInfo(code);

            if (save) SaveCode(code);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string LoadSavedCode()
        {
            try
            {
                if (File.Exists(SettingFile))
                    return File.ReadAllText(SettingFile).Trim();
            }
            catch { }
            return "";
        }

        private static void SaveCode(string code)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingFile)!);
                File.WriteAllText(SettingFile, code);
            }
            catch { }
        }
    }

    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string keyword, string displayName)
        {
            Code = code;
            Keyword = keyword;
            DisplayName = displayName;
        }

        public string Code { get; }          // "ja-JP"
        public string Keyword { get; }       // "Japanese" - gõ trên command line
        public string DisplayName { get; }   // "日本語"
    }
}