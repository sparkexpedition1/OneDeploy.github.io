using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AutoPrepareMLNotebook.Models
{
    public class Grid
    {
        public int SNo { get; set; }
        public string Operation { get; set; }
        public List<string> Tables { get; set; }
        public List<string> Columns { get; set; }
        public List<string> CommentCell { get; set; }
        public List<string> CodeCell { get; set; }
        public Dictionary<string,List<string>> Mapping { get; set; }
        public int ParentSNo { get; set; }
    }
    public class CodeBlock
    {
        public List<string> comment { get; set; }

    }
}