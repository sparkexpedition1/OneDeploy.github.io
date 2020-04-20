using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AutoPrepareMLNotebook.Models
{
    

    public class Metadata
    {
    }

    public class Cell
    {
        public string cell_type { get; set; }
        public string UniqueId { get; set; }
        public Metadata metadata { get; set; }
        public List<string> source { get; set; }
        public int? execution_count { get; set; }
        public List<object> outputs { get; set; }
        public bool ShouldSerializeexecution_count()
        {
            return execution_count != null;
        }
    }

    public class Kernelspec
    {
        public string display_name { get; set; }
        public string language { get; set; }
        public string name { get; set; }
    }

    public class CodemirrorMode
    {
        public string name { get; set; }
        public int version { get; set; }
    }

    public class LanguageInfo
    {
        public CodemirrorMode codemirror_mode { get; set; }
        public string file_extension { get; set; }
        public string mimetype { get; set; }
        public string name { get; set; }
        public string nbconvert_exporter { get; set; }
        public string pygments_lexer { get; set; }
        public string version { get; set; }
    }

    public class Metadata2
    {
        public Kernelspec kernelspec { get; set; }
        public LanguageInfo language_info { get; set; }
        public string name { get; set; }
        public long notebookId { get; set; }
    }

    public class Notebook
    {
        public List<Cell> cells { get; set; }
        public Metadata2 metadata { get; set; }
        public int nbformat { get; set; }
        public int nbformat_minor { get; set; }
        public string NotebookName { get; set; }
    }
}