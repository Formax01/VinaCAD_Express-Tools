using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            { error = "Chiều dài L và bán kính r phải lớn hơn 0."; return false; }
            if (InnerRadius >= Length)
            { error = $"Bán kính r ({InnerRadius:0.##}) phải nhỏ hơn L ({Length:0.##})."; return false; }
            return true;
        }
    }
}
