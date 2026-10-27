using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Resources.Language;
namespace Tools.VinaCad.Modeling
{
    public class TurningVaneSetting
    {
        public double Length { get; set; } = 100;       // L: chiều dài mỗi cạnh dẫn từ tâm quay
        public double InnerRadius { get; set; } = 50;   // r: bán kính cung nhỏ (R50)
        public string LayerName { get; set; } = "";     // rỗng = layer hiện hành

        public TurningVaneSetting Clone() => (TurningVaneSetting)MemberwiseClone();

        public bool TryValidate(out string error)
        {
            error = null;
            if (Length <= 0 || InnerRadius <= 0)
            { error = Lang.CLG_UI_LvaRphailonhon0; return false; }
            if (InnerRadius >= Length)
            {
                error = string.Format(
        Lang.CLG_MessDialog_BanKinhNhoHonChieuDai,
        InnerRadius,
        Length
    );
                return false;
            }
            return true;
        }
    }
}
