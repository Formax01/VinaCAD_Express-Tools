using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace Tools.Model
{
    public class RenameTextModel
    {
        public ObjectId TextId { get; set; }
        public string Text { get; set; }
        public string NewText { get; set; }
    }
}
