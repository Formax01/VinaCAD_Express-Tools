using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace Tools.Model
{
    public class RenameLayerModel
    {
        public ObjectId LayerId { get; set; }
        public string NameLayer { get; set; }
        public string RenameLayer { get; set; }
    }
}
