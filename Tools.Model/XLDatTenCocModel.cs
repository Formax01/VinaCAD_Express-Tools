using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace Tools.Model
{
    public class XLDatTenCocModel
    {
        public ObjectId CocId { get; set; }
        public string SoHieu { get; set; } = string.Empty;
        public string TenCoc { get; set; } = string.Empty;
    }
}
