using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace Tools.VinaCad.Modeling
{
    public class BlockTextPair
    {
        public ObjectId BlockId { get; set; }
        public ObjectId TextId { get; set; }
        public double Distance { get; set; }
    }
}
