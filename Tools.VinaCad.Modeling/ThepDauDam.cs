using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace Tools.VinaCad.Modeling
{
        public class ThepDauDam
        {
            public BlockReference blockThep { get; set; }
            public string dangThep { get; set; }
            public double soHieuLaSoDeSort { get; set; }
            public string soHieu { get; set; }
            public double duongKinh { get; set; }
            public int soLuong { get; set; }
            public double chieuDaiThanhThep { get; set; }
            public double chieuDaiBeMoc { get; set; }
        public static int CompareSoHieuNatural(string a, string b)
        {
            a = a?.Trim() ?? "";
            b = b?.Trim() ?? "";

            if (a.Length == 0 && b.Length == 0)
                return 0;

            if (a.Length == 0)
                return 1;

            if (b.Length == 0)
                return -1;

            int ia = 0;
            int ib = 0;

            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia];
                char cb = b[ib];

                bool isDigitA = char.IsDigit(ca);
                bool isDigitB = char.IsDigit(cb);

                if (isDigitA && isDigitB)
                {
                    string numA = ReadNumber(a, ref ia);
                    string numB = ReadNumber(b, ref ib);

                    long valueA;
                    long valueB;

                    bool okA = long.TryParse(numA, out valueA);
                    bool okB = long.TryParse(numB, out valueB);

                    if (okA && okB)
                    {
                        int cmpNum = valueA.CompareTo(valueB);

                        if (cmpNum != 0)
                            return cmpNum;
                    }
                    else
                    {
                        int cmpTextNum = string.Compare(numA, numB, StringComparison.OrdinalIgnoreCase);

                        if (cmpTextNum != 0)
                            return cmpTextNum;
                    }
                }
                else
                {
                    int cmpChar = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));

                    if (cmpChar != 0)
                        return cmpChar;

                    ia++;
                    ib++;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        private static string ReadNumber(string text, ref int index)
        {
            int start = index;

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            return text.Substring(start, index - start);
        }
    }
       
}
