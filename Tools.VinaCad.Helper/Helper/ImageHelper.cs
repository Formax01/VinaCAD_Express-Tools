using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Tools.VinaCad.Helper.Helper
{
    public class ImageHelper
    {
        public static string GetImagePath(string imageFileName)
        {
            string dllFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            DirectoryInfo dir = new DirectoryInfo(dllFolder);

            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Resources")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
                throw new DirectoryNotFoundException("Không tìm thấy thư mục Resources.");

            return Path.Combine(dir.FullName, "Resources", "Images", imageFileName);
        }
    }
}
