using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AutoPrepareMLNotebook.Models
{
    public class DataModel
    {
        public string Id { get; set; }
        public string text { get; set; }
        public string tooltiptext { get; set; }
        public bool IsChecked { get; set; }
        public List<column> children { get; set; }
    }
    public class column
    {
        public string Id { get; set; }
        public string text { get; set; }
        public string tooltiptext { get; set; }
    }
}