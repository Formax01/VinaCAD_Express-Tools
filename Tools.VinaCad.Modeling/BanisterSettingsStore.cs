using System;
using System.IO;
using System.Text.Json;
using Tools.Model;

namespace Tools.VinaCad.Modeling
{
    public static class BanisterSettingsStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VinaCAD", "ExpressTools", "lg-settings.json");

        public static BanisterInput Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var input = JsonSerializer.Deserialize<BanisterInput>(File.ReadAllText(FilePath));
                    if (input != null) return input;
                }
            }
            catch { /* file hỏng hoặc không đọc được: dùng mặc định */ }
            return new BanisterInput();
        }

        public static void Save(BanisterInput input)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var opt = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(input, opt));
            }
            catch { /* lưu lỗi không được làm hỏng lệnh vẽ */ }
        }
    }
}