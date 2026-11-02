using System.Diagnostics;
using Tools.Resources.Language;

namespace Tools.VinaCad.Action.Actions
{
    public sealed class FeedbackAction
    {
        public void Execute()
        {
            string url = Translator.Instance.CurrentCode switch
            {
                "en-US" => "https://forms.gle/sMRSJYDS9fFxERaH6", // tiếng anh
                "ja-JP" => "https://forms.gle/wzuCpHVD49hjbqPu8", // tiềng nhật
                _ => "https://forms.gle/zFGFVbNazxBiMDg1A"        // tiếng việt
            };

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
