using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using AutoPrepareMLNotebook.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Azure;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;

namespace AutoPrepareMLNotebook.Controllers
{
    public class NotebookController : Controller
    {
        static string storageConnection = CloudConfigurationManager.GetSetting("BlobStorageConnectionString");
        static CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(storageConnection);
        static string containerName = "notebooktemplates";
        static CloudBlobClient blobClient = cloudStorageAccount.CreateCloudBlobClient();
        CloudBlobContainer cloudBlobContainer = blobClient.GetContainerReference(containerName);
        public static string connectionstring = "Server=tcp:selfservices.database.windows.net,1433;Initial Catalog=AdventureWorksDW2017;Persist Security Info=False;User ID=sqllogin;Password=Infy123+;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        string referencepath = @"D:\AutoML-Prep\";
        static DataTable dt = new DataTable();
        public ActionResult AutoMLPrepNotebook()
        {
           // LoadTemplates();
            return View();
        }
        public List<Notebook> LoadTemplates()
        {
            List<Notebook> notebooks = new List<Notebook>();

            string[] files = Directory.GetFiles(Server.MapPath(@"~\Content\Templates\"), "*.ipynb");

            if (files.Count() != 0)
            {
                foreach (var file in files)
                {
                    string text = System.IO.File.ReadAllText(file);
                    Notebook note = JsonConvert.DeserializeObject<Notebook>(text);

                    if (note.cells.Count>0)
                    {
                        string uniqueidtext = "";
                        foreach (var cell in note.cells)
                        {
                            if(cell.cell_type!="code")
                            {
                                uniqueidtext = Regex.Match(string.Join(",", cell.source), @"UNIQUEID-([\w\s+]*)").Groups[1].ToString().Replace(@"\n", "");
                            }
                            cell.UniqueId = uniqueidtext;
                        }
                    }
                    note.NotebookName = Path.GetFileName(file);
                    notebooks.Add(note);
                }
            }
            return notebooks;
        }
        public JsonResult GetOperations()
        {
            GetDatabase();
            string[] files = Directory.GetFiles(Server.MapPath(@"~\Content\Templates\"),"*.ipynb");
            List<DataModel> TemplateList = new List<DataModel>();
            if (files.Count()!=0)
            {
                foreach (var file in files)
                {
                    DataModel newtemplate = new DataModel();
                    List<column> functions = new List<column>();
                    var notebooktemplate = System.IO.File.ReadAllText(file);
                    dynamic template = JsonConvert.DeserializeObject<dynamic>(notebooktemplate);
                    List<string> code = new List<string>();
                    List<dynamic> cellslist = new List<dynamic>();
                    if (notebooktemplate.Contains("worksheets"))
                    { cellslist = template.worksheets[0].cells.ToObject<List<dynamic>>(); }
                    else { cellslist = template.cells.ToObject<List<dynamic>>(); }
                    var markdowncells = cellslist.Where(x => x.cell_type == "markdown").Select(z => z.source.ToString()).ToList();
                    var oper = markdowncells.Select(y => Regex.Match(y,@"\""UNIQUEID-([\w\s+]*)").Groups[1].ToString().Replace(@"\n", "")).ToList();
                    newtemplate.Id = Path.GetFileNameWithoutExtension(file);
                    newtemplate.text = Path.GetFileNameWithoutExtension(file);
                    newtemplate.tooltiptext = Path.GetFileNameWithoutExtension(file);
                    foreach (var op in oper)
                    {
                        if(op!="")
                        {
                            column column = new column();
                            column.Id = Path.GetFileNameWithoutExtension(file) + "." + op;
                            column.text = op;
                            var nre=markdowncells.Where(y => y.Contains("UNIQUEID-" + op)).Select(y => y).ToList();
                            column.tooltiptext = nre[0].Replace("\"", "").Replace("\\n", "<br>").Replace("[", "").Replace("]", "").Replace(",", "");
                            functions.Add(column);
                        }
                        
                    }
                    newtemplate.children = functions;
                    newtemplate.IsChecked = false;
                    TemplateList.Add(newtemplate);
                }
            }
            return Json(TemplateList, JsonRequestBehavior.AllowGet);
        }
        public void GetDatabase()
        {
            DataTable result = new DataTable();
            string query = "select schema_name(tab.schema_id) as schema_name,tab.name as table_name,col.column_id,col.name as column_name, " +
                "t.name as data_type from sys.tables as tab inner join sys.columns as col on " +
                "tab.object_id = col.object_id left join sys.types as t on col.user_type_id = t.user_type_id order by schema_name," +
                " table_name, column_id;";
            using (SqlConnection conn = new SqlConnection(connectionstring))
            {
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        result.Load(reader);
                    }
                    conn.Close();
                }
            }
            dt = result;
        }
        public JsonResult TestDataSource(string operation)
        {
            List<DataModel> Tableslist = new List<DataModel>();
            try
            {
                var distinctValues = dt.AsEnumerable()
                        .Select(row => new
                        {
                            TableName = row.Field<string>("table_name"),
                        })
                        .Distinct();
                string[] files = Directory.GetFiles(Server.MapPath(@"~\Content\Templates\"), "*.ipynb");
                var reqfile = files.Where(x => Path.GetFileNameWithoutExtension(x) == operation.Split('.')[0]);
                var notebooktemplate = System.IO.File.ReadAllText(reqfile.ToList()[0]);
                dynamic template = JsonConvert.DeserializeObject<dynamic>(notebooktemplate);
                List<string> code = new List<string>();
                IEnumerable<dynamic> cells = template.cells;
                var operationcell= cells.ToList().FindIndex(x => x.cell_type == "markdown" && x.source.ToString().Contains(operation.Split('.')[1].ToString()));
                var templatecell = cells.Skip(operationcell+1).Take(1).Select(x => x.source).First().ToString();
                if (distinctValues != null)
                {
                    
                    if (!templatecell.Contains( "##COLUMNNAME##"))
                    {
                        foreach (var value in distinctValues)
                        {
                            DataModel table = new DataModel();
                            List<column> cols = new List<column>();
                            table.Id = value.TableName;
                            table.text = value.TableName;
                            table.children = cols;
                            table.IsChecked = false;
                            Tableslist.Add(table);
                        }
                    }
                    else
                    {
                        foreach (var value in distinctValues)
                        {
                            DataModel table = new DataModel();
                            List<column> cols = new List<column>();
                            var name = dt.AsEnumerable().Where(row => Convert.ToString(row["table_name"]) == value.TableName).Select(row => row.Field<string>("column_name")).ToList();
                            foreach (var col in name)
                            {
                                column column = new column();
                                column.Id = value.TableName + "." + col;
                                column.text = col;
                                cols.Add(column);
                            }
                            table.Id = value.TableName;
                            table.text = value.TableName;
                            table.children = cols;
                            table.IsChecked = false;
                            Tableslist.Add(table);
                        }
                    }

                }

                return Json(Tableslist, JsonRequestBehavior.AllowGet);

            }
            catch (Exception ex)
            {

                return Json(Tableslist, JsonRequestBehavior.AllowGet);
            }


        }
        public JsonResult CheckJsonData(string tables)
        {
            var count = 0;
            string path = Server.MapPath(@"~/Content/Projects/AutoPrepGrid.json");
            string json = System.IO.File.ReadAllText(path);
            List<Grid> data = JsonConvert.DeserializeObject<List<Grid>>(json);
            var init = data.Where(x=>x.Operation.ToString().Contains("Data Ingestion")).Select(x=>x.Tables).ToList();
            var table = tables.Split(',').ToList();
            foreach(var item in table)
            {
               var exists= init.Where(x => x.Contains(item)).ToList();
                if (exists.Count == 0)
                { count = 1; }
            }
            if(count==0)
            {
                return Json(false, JsonRequestBehavior.AllowGet);

            }
            else
            {
                return Json(true, JsonRequestBehavior.AllowGet);

            }
        }
        public JsonResult AddToExistingCell(List<string> checkedIds, string operation, List<string> code)
        {
            string path = Server.MapPath(@"~/Content/Projects/AutoPrepGrid.json");
            string json = System.IO.File.ReadAllText(path);
            List<Grid> data = JsonConvert.DeserializeObject<List<Grid>>(json);
            Grid grid = new Grid();
            int parent = 0;int exists = 0;
            var ischildren = data.Where(x => x.Operation == operation).Select(x => x.SNo).ToList();
            if (ischildren.Count>0)
            {
                parent = ischildren.FirstOrDefault();
                exists = 1;
            }
            List<string> tables = new List<string>();
            List<string> columns = new List<string>();
            Dictionary<string, List<string>> Mapping = new Dictionary<string, List<string>>();
            try
            {
                if (checkedIds != null)
                {
                    foreach (var item in checkedIds)
                    {
                        if (item.Contains("."))
                        {
                            tables.Add(item.ToString().Split('.')[0]);
                        }
                        else
                        {
                            tables.Add(item.ToString());
                        }
                    }
                    if (tables != null)
                    {
                        foreach (var tbl in tables.Distinct())
                        {
                            var cols = checkedIds.Where(x => x.Contains('.') && x.Contains(tbl)).Select(x => x.ToString().Split('.')[1]).ToList();
                            Mapping.Add(tbl, cols);
                            columns.AddRange(cols);

                        }
                    }

                }
                grid.Operation = operation;
                grid.Columns = columns;
                grid.Mapping = Mapping;
                grid.Tables = tables.Distinct().ToList<string>();
                grid.ParentSNo = parent;
                grid.CommentCell = null;
                grid.CodeCell = code;
                if (data != null && exists==0)
                {
                    grid.SNo = data.Count + 1;
                    data.Add(grid);
                }
                else if(exists==1)
                {
                    string childpath = Server.MapPath(@"~/Content/Projects/AutoPrepGridChildren.json");
                    string childjson = System.IO.File.ReadAllText(childpath);
                    List<Grid> childdata = JsonConvert.DeserializeObject<List<Grid>>(childjson);
                    if (childdata != null) {
                        grid.SNo = childdata.Count + 1;
                        childdata.Add(grid);
                    }
                    else
                    {
                        grid.SNo = 1;
                        childdata = new List<Grid>();
                        childdata.Add(grid);
                    }
                    var childoutput = JsonConvert.SerializeObject(childdata);
                    System.IO.File.WriteAllText(childpath, childoutput);
                }
                else
                {
                    grid.SNo = 1;
                    data = new List<Grid>();
                    data.Add(grid);
                }
                var output = JsonConvert.SerializeObject(data);
                System.IO.File.WriteAllText(path, output);
                bool hasworksheet = false;
                if (System.IO.File.Exists(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb"))
                {
                    string ipython = System.IO.File.ReadAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb");
                    dynamic template = JsonConvert.DeserializeObject<dynamic>(ipython);
                    int cellcount ;
                    List<dynamic> cellslist = new List<dynamic>();
                    if (template.ToString().Contains("worksheets"))
                    { cellslist = template.worksheets[0].cells.ToObject<List<dynamic>>();
                        cellcount = template.worksheets[0].cells.Count;
                        hasworksheet = true;
                    }
                    else { cellslist = template.cells.ToObject<List<dynamic>>();
                        cellcount = template.cells.Count;
                    }
                    if (exists == 1)
                    {
                        var operationcell = cellslist.ToList().FindIndex(x => x.cell_type == "markdown" && x.source.ToString().Contains(operation.Split('.')[1].ToString()));
                        List<string> codedem = cellslist.Skip(operationcell + 1).Take(1).Select(x => x.source).ToList()[0].ToObject<List<string>>();
                        codedem.AddRange(code);
                        Cell cell1 = FormCell(codedem);
                        cell1.cell_type = "code";
                        var newcell = JObject.Parse(JsonConvert.SerializeObject(cell1));
                        cellslist.RemoveAt(operationcell);
                        cellslist.Insert(operationcell, newcell);
                        var tempcells = JArray.Parse(JsonConvert.SerializeObject(cellslist));
                        if (hasworksheet) template.worksheets[0].cells = tempcells;
                        else {
                            template.cells = tempcells;
                            }
                    }
                    else 
                    {
                        Cell cell1 = FormCell(code);
                        var newcell = JObject.Parse(JsonConvert.SerializeObject(cell1));
                        cellslist.Insert(cellcount, newcell);
                        var tempcells = JArray.Parse(JsonConvert.SerializeObject(cellslist));
                        template.cells = tempcells;
                    }
                    
                    
                    string outnote = JsonConvert.SerializeObject(template);
                    //string filename = @"AutoPrepareMLNotebook.ipynb";
                    System.IO.File.WriteAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb", outnote);

                }
                else
                {
                    Notebook note = new Notebook();
                    List<Cell> cells = new List<Cell>();
                    Metadata2 newmeta = new Metadata2();
                    List<string> input = new List<string>() { "<span style='color:blue;font-weight:bold;font-size:20px'> This is an AutoGenerated Notebook from Data Model</span>" };
                    Cell cell1 = FormCell(input);
                    cell1.cell_type = "markdown";
                    cells.Add(cell1);
                    List<string> init = new List<string>() { "!pip install --user --no-warn-script-location azureml.dataprep", "!pip install --user pyspark", "import azureml.dataprep as dprep" };
                    Cell initcell = FormCell(init);
                    cell1.cell_type = "code";
                    cells.Add(initcell);
                    Cell codecell = FormCell(code);
                    cell1.cell_type = "code";
                    cells.Add(codecell);
                    Kernelspec kernel = new Kernelspec();
                    kernel.display_name = "Python 3"; kernel.language = "python"; kernel.name = "python3"; CodemirrorMode codemirror = new CodemirrorMode();
                    LanguageInfo lang = new LanguageInfo();
                    codemirror.name = "ipython"; codemirror.version = 3;
                    lang.codemirror_mode = codemirror;
                    lang.file_extension = ".py"; lang.mimetype = "text/x-python"; lang.name = "python"; lang.nbconvert_exporter = "python"; lang.pygments_lexer = "ipython3";
                    lang.version = "3.7.3";
                    newmeta.language_info = lang; newmeta.kernelspec = kernel;
                    note.metadata = newmeta; note.cells = cells;
                    note.nbformat = 4;
                    note.nbformat_minor = 1;
                    string outnote = JsonConvert.SerializeObject(note);
                    //string filename = @"AutoPrepareMLNotebook.ipynb";
                    System.IO.File.WriteAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb", outnote);

                }
                return Json(data, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {

                return Json(false, JsonRequestBehavior.AllowGet);
            }
        }
        public JsonResult AddToRecipe(List<string> checkedIds, string operation, List<string> comment,List<string> code)
        {
            
            string path = Server.MapPath(@"~/Content/Projects/AutoPrepGrid.json");
            string json = System.IO.File.ReadAllText(path);
            List<Grid> data = JsonConvert.DeserializeObject<List<Grid>>(json);
            Grid grid = new Grid();
            List<string> tables = new List<string>();
            List<string> columns = new List<string>();
            
            Dictionary<string, List<string>> Mapping = new Dictionary<string, List<string>>();
            try
            {
                if (checkedIds != null)
                {
                    foreach (var item in checkedIds)
                    {
                        if (item.Contains("."))
                        {
                            tables.Add(item.ToString().Split('.')[0]);
                        }
                        else
                        {
                            tables.Add(item.ToString());
                        }
                    }
                    if (tables != null)
                    {
                        foreach (var tbl in tables.Distinct())
                        {
                            var cols = checkedIds.Where(x => x.Contains('.') && x.Contains(tbl)).Select(x => x.ToString().Split('.')[1]).ToList();
                            Mapping.Add(tbl, cols);
                            columns.AddRange(cols);

                        }
                    }

                }
                grid.Operation = operation;
                grid.Columns = columns;
                grid.Mapping = Mapping;
                grid.Tables = tables.Distinct().ToList<string>();
                grid.ParentSNo = 0;
                grid.CodeCell = code;
                grid.CommentCell = comment;
                if (data != null)
                {
                    grid.SNo = data.Count + 1;
                    data.Add(grid);
                }
                else
                {
                    grid.SNo = 1;
                    data = new List<Grid>();
                    data.Add(grid);
                    
                }
                var output = JsonConvert.SerializeObject(data);
                System.IO.File.WriteAllText(path, output);
                if (System.IO.File.Exists(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb"))
                {
                    string ipython = System.IO.File.ReadAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb");
                    dynamic template = JsonConvert.DeserializeObject<dynamic>(ipython);
                    Metadata newmeta = new Metadata(); int cellcount;
                    List<dynamic> cellslist = new List<dynamic>();
                    if (template.ToString().Contains("worksheets"))
                    {
                        cellslist = template.worksheets[0].cells.ToObject<List<dynamic>>();
                        cellcount = template.worksheets[0].cells.Count;
                    }
                    else
                    {
                        cellslist = template.cells.ToObject<List<dynamic>>();
                        cellcount = template.cells.Count;
                    }
                    Cell cell1 = FormCell(comment);
                    cell1.cell_type = "markdown";
                    Cell cell2 = FormCell(code);
                    cell2.cell_type = "code";
                    var commcell = JObject.Parse(JsonConvert.SerializeObject(cell1));
                    var newcell = JObject.Parse(JsonConvert.SerializeObject(cell2));
                    cellslist.Insert(cellcount, commcell);
                    cellslist.Insert(cellcount + 1, newcell);
                    var tempcells = JArray.Parse(JsonConvert.SerializeObject(cellslist));
                    template.cells = tempcells;
                    string outnote = JsonConvert.SerializeObject(template);
                    //string filename = @"AutoPrepareMLNotebook.ipynb";
                    System.IO.File.WriteAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb", outnote);

                }
                else
                {

                    Notebook note = new Notebook();
                    List<Cell> cells = new List<Cell>();
                    Metadata2 newmeta = new Metadata2();
                    List<string> input = new List<string>() { "<span style='color:blue;font-weight:bold;font-size:20px'> This is an AutoGenerated Notebook from Data Model</span>" };
                    Cell cell1 = FormCell(input);
                    cell1.cell_type = "markdown";
                    cells.Add(cell1);
                    List<string> init = new List<string>() { "!pip install --user --no-warn-script-location azureml.dataprep", "!pip install --user pyspark", "import azureml.dataprep as dprep" };
                    Cell initcell = FormCell(init);
                    initcell.cell_type = "code";
                    cells.Add(initcell);
                    var comm = FormCell(comment);
                    comm.cell_type = "markdown";
                    cells.Add(comm);
                    Cell codecell = FormCell(code);
                    codecell.cell_type = "code";
                    cells.Add(codecell);
                    Kernelspec kernel = new Kernelspec();
                    kernel.display_name = "Python 3"; kernel.language = "python"; kernel.name = "python3"; CodemirrorMode codemirror = new CodemirrorMode();
                    LanguageInfo lang = new LanguageInfo();
                    codemirror.name = "ipython"; codemirror.version = 3;
                    lang.codemirror_mode = codemirror;
                    lang.file_extension = ".py"; lang.mimetype = "text/x-python"; lang.name = "python"; lang.nbconvert_exporter = "python"; lang.pygments_lexer = "ipython3";
                    lang.version = "3.7.3";
                    newmeta.language_info = lang; newmeta.kernelspec = kernel;
                    note.metadata = newmeta;note.cells = cells;
                    note.nbformat = 4;
                    note.nbformat_minor = 1;
                    string outnote = JsonConvert.SerializeObject(note);
                    //string filename = @"AutoPrepareMLNotebook.ipynb";
                    System.IO.File.WriteAllText(@"G:\AutoML-Prep\AutoPrepareMLNotebook.ipynb", outnote);
                }
                return Json(data, JsonRequestBehavior.AllowGet);
                
            }
            catch (Exception ex)
            {

                return Json(false, JsonRequestBehavior.AllowGet);
            }
        }

        public JsonResult PreviewCode(List<string> checkedIds, string operation, int existingcell)
        {
            string[] files = Directory.GetFiles(Server.MapPath(@"~\Content\Templates\"), "*.ipynb");
            var reqfile = files.Where(x => Path.GetFileNameWithoutExtension(x) == operation.Split('.')[0]);
            var notebooktemplate = System.IO.File.ReadAllText(reqfile.ToList()[0]);
            dynamic template = JsonConvert.DeserializeObject<dynamic>(notebooktemplate);
            List<string> code = new List<string>();
            IEnumerable<dynamic> cells = template.cells;
            var operationcell = cells.ToList().FindIndex(x => x.cell_type == "markdown" && x.source.ToString().Contains(operation.Split('.')[1].ToString()));
            var mardowncell =cells.ToList().Where(x => x.cell_type == "markdown" && x.source.ToString().Contains(operation.Split('.')[1].ToString())).Select(x => x.source).ToList()[0].ToObject<string[]>();
            code = cells.Skip(operationcell + 1).Take(1).Select(x => x.source).ToList()[0].ToObject<List<string>>();
            var templatecell = cells.Skip(operationcell + 1).Take(1).Select(x => x.source).First().ToString();
            List<string> tables = new List<string>();
            List<string> columns = new List<string>();
            Dictionary<string, List<string>> Mapping = new Dictionary<string, List<string>>();

            if (checkedIds != null)
            {
                foreach (var item in checkedIds)
                {
                    if (item.Contains("."))
                    {
                        tables.Add(item.ToString().Split('.')[0]);
                    }
                    else
                    {
                        tables.Add(item.ToString());
                    }
                }
                if (tables != null)
                {
                    foreach (var tbl in tables.Distinct())
                    {
                        var cols = checkedIds.Where(x => x.Contains('.') && x.Contains(tbl)).Select(x => x.ToString().Split('.')[1]).ToList();
                        Mapping.Add(tbl, cols);
                        columns.AddRange(cols);

                    }
                }

            }
            int count = 0;
            List<string> singlecell = new List<string>();
            var startindex = code.FindIndex(x => x.Contains("##REPEAT REGION STARTS##"));
            var endindex = code.FindIndex(x => x.Contains("##REPEAT REGION ENDS##"));
            singlecell = code.Where(x => code.IndexOf(x) > startindex && code.IndexOf(x) < endindex).ToList();
            //singlecell = code.Where(x => x.Contains("##singlecell##")).Select(x => x.Replace("##singlecell##", "")).ToList();
            if (singlecell.Count == 0)
            {
                singlecell = code;
            }
            foreach (var tbl in Mapping)
            {
                if (count == 0)
                {
                    if (!templatecell.Contains("##COLUMNNAME##"))
                    {
                        code = code.Select(x => x.Replace("##TABLENAME##", tbl.Key).Replace("##REPEAT REGION STARTS##", "").Replace("##REPEAT REGION ENDS##", "")).ToList();
                        count = 1;
                    }
                    else
                    {
                        List<string> cols = new List<string>();
                        foreach (var col in tbl.Value)
                        {
                            cols.AddRange(code.Select(x => x.Replace("##TABLENAME##", tbl.Key).Replace("##COLUMNNAME##", col)).ToList());
                            cols.Add("\n");
                        }
                        code = cols;
                        count = 1;
                    }
                }
                else
                {
                    if (!templatecell.Contains("##COLUMNNAME##"))
                    {
                        List<string> tempcode = new List<string>();
                        //tempcode=singlecell;
                        tempcode.Add("\n");
                        foreach (var val in singlecell)
                        {
                            tempcode.Add(val.Replace("##TABLENAME##", tbl.Key));
                        }
                        //tempcode.ForEach(x => { x=x.Replace("##TABLENAME##", tbl.Key);});
                        code.AddRange(tempcode);
                    }
                    else
                    {
                        foreach (var col in tbl.Value)
                        {
                            List<string> tempcode = new List<string>();
                            tempcode.Add("\n");
                            foreach (var val in singlecell)
                            {
                                tempcode.Add(val.Replace("##TABLENAME##", tbl.Key).Replace("##COLUMNNAME##", col));
                            }
                            code.AddRange(tempcode);
                        }
                        count = 1;
                    }
                }
            }
            if(existingcell==1)
            {
                AddToExistingCell(checkedIds, operation, code);
            }
            if(existingcell==0)
            {
                List<string> commentlist = new List<string>(mardowncell);
                AddToRecipe(checkedIds, operation, commentlist, code);
            }
            return Json(code, JsonRequestBehavior.AllowGet);
        }
        public void DownloadFilefromPath(string path)
        {
            try
            {
               
                WebClient req = new WebClient();
                HttpResponse response = System.Web.HttpContext.Current.Response;
                response.Clear();
                response.ClearContent();
                response.ClearHeaders();
                response.Buffer = true;
                response.AddHeader("Content-Disposition", "attachment;filename=\"" + Server.MapPath(path) + "\"");
                byte[] data = req.DownloadData(Server.MapPath(path));
                response.BinaryWrite(data);
                response.End();
            }
            catch (Exception ex)
            {
            }
        }
        public ActionResult DownloadFile(string path)
        {
            byte[] fileBytes = System.IO.File.ReadAllBytes(path);
            string fileName = Path.GetFileNameWithoutExtension(path)+".ipynb";
            return File(fileBytes, System.Net.Mime.MediaTypeNames.Application.Octet, fileName);
        }
        public JsonResult DeleteOperation(int SNo)
        {
            string path = Server.MapPath(@"~\Content\Projects\AutoPrepGrid.json");
            List<Grid> grid = JsonConvert.DeserializeObject<List<Grid>>(System.IO.File.ReadAllText(path));
            var data = grid.Where(x => x.SNo != SNo).ToList();
            var output = JsonConvert.SerializeObject(data);
            System.IO.File.WriteAllText(path, output);
            return Json(true, JsonRequestBehavior.AllowGet);
        }
        public JsonResult CreateTemplate()
        {
            string path = Server.MapPath(@"~\Content\Notebooks\NewTemplate");
            HttpFileCollectionBase files = Request.Files;
            string destpath = "";
            if (files.Count > 0)
            {
                string[] filesplit = files.Keys[0].Split(':');
                files[0].SaveAs(path + @"\" + filesplit[1] + @"\" + filesplit[0]);
                var notebooktemplate = System.IO.File.ReadAllText(path + @"\" + filesplit[1] + @"\" + filesplit[0]);
                dynamic template = JsonConvert.DeserializeObject<dynamic>(notebooktemplate);
                List<dynamic> cellslist = new List<dynamic>();
                var type = 0;
                if (notebooktemplate.Contains("worksheets"))
                { cellslist = template.worksheets[0].cells.ToObject<List<dynamic>>();type = 1; }
                else { cellslist = template.cells.ToObject<List<dynamic>>(); }
                //int markcount = 0; int exists = 0; int cellcount = 0;
                //var generatedcells = cellslist.ToList();

                //cellslist.ForEach(y =>
                //{

                //    if (y.cell_type == "markdown")
                //    {
                //        exists = 1;
                //        cellcount = cellcount + 1;
                //        y.prompt_number = cellcount + markcount;
                //    }
                //    else
                //    {
                //        cellcount = cellcount + 1;
                //        if (exists == 0)
                //        {
                            
                //            string[] empty = new string[] {};
                //            dynamic newcomment = CreateCell(empty);
                //            newcomment.prompt_number = cellcount + markcount;
                //            var comm = JObject.Parse(JsonConvert.SerializeObject(newcomment));
                //            generatedcells.Insert(cellcount-1, comm);
                //            y.prompt_number = cellcount + markcount + 1;
                //            markcount = markcount + 1;

                //        }
                //        else
                //        {
                //            y.prompt_number = cellcount;
                //        }
                //        exists = 0;
                //    }
                //});

                cellslist.ForEach(x=>
                {
                    if (x.cell_type=="markdown" && !(x.source.ToString().Contains("---")))
                    {
                        List<string> updated = new List<string>();
                        updated = x.source.ToObject<List<string>>();
                        updated.Insert(0, "<span style='color:blue;font-weight:bold;font-size:15px'>UNIQUEID-{Enter Function Name here..}</span><br>");
                        var r = JsonConvert.SerializeObject(updated);
                        x.source = JArray.Parse(r);
                        x.prompt_number = x.prompt_number + 1;
                    }
                    else
                    {
                        x.prompt_number = x.prompt_number + 1;
                    }
                });
                List<string> guidelines = new List<string>() {
                        "<span style='color:blue;font-weight:bold;font-size:15px'>Guidelines to create a template:</span><br>",
                        "<span style='color:purple;font-style:italic;font-size:13px'>> Every cell should be independent and have single functionality</span><br>",
                        "<span style='color:purple;font-style:italic;font-size:13px'>> Notebook should have separate pip and code initialization cells</span><br>",
                        "<span style='color:purple;font-style:italic;font-size:13px'>> Every function should have comment and code cell</span><br>",
                        "<span style='color:purple;font-style:italic;font-size:13px'>> Each code cell should have maximum 20 lines</span><br>",
                        "<span style='color:purple;font-style:italic;font-size:13px'>> Replace variables like TableNames, ColumnNames with ##TABLENAME##,##COLUMNNAME## etc.</span><br>"};
                dynamic guidelinescell = FormCell(guidelines);
                var newcell=JObject.Parse(JsonConvert.SerializeObject(guidelinescell));
                cellslist.Insert(0, newcell);
                var tempcells= JArray.Parse(JsonConvert.SerializeObject(cellslist));
                if (type==1)
                {
                    template.worksheets[0].cells = tempcells;
                }
                else
                {
                    template.cells= tempcells;
                }
                destpath = Server.MapPath(@"/Content/Notebooks/" + filesplit[1] + @"/" + filesplit[0]);
                System.IO.File.WriteAllText(destpath,JsonConvert.SerializeObject(template));
            //}
                
            }

            return Json(destpath,JsonRequestBehavior.AllowGet);
        }
        public JsonResult VerifyTemplate()
        {
            List<Notebook> notebooks = LoadTemplates();
            string version = System.DateTime.Now.ToShortDateString().Replace(@"/", "") ;
            string path = Server.MapPath(@"~\Content\Notebooks\Verify");
            HttpFileCollectionBase files = Request.Files;
            string ValidationLogFile = "LogFile-" + DateTime.Now.ToShortDateString().Replace(@"/", "") +"_"+ DateTime.Now.ToShortTimeString().Replace(@":", "").Replace(" ","") + ".log";
            string ValidationLogFilePath = Server.MapPath(@"~\Content\ValidationLogs\" + ValidationLogFile);
            int errorcount = 0;int warningcount = 0;
            if (files.Count > 0)
            {
                string notebooktemplate= new StreamReader(files[0].InputStream).ReadToEnd();
                
                Notebook NewUploadedNotebook = JsonConvert.DeserializeObject<Notebook>(notebooktemplate);

                if (NewUploadedNotebook.cells.Count > 0)
                {
                    string uniqueidtext = "";

                    foreach (var cell in NewUploadedNotebook.cells)
                    {
                        if (cell.cell_type != "code")
                        {
                            uniqueidtext = Regex.Match(string.Join(",", cell.source), @"UNIQUEID-([\w\s+]*)").Groups[1].ToString().Replace(@"\n", "");
                        }
                        cell.UniqueId = uniqueidtext;
                    }
                    NewUploadedNotebook.NotebookName = files[0].FileName;
                }
                Dictionary<string, List<string>> uniqueidslist = new Dictionary<string, List<string>>();
                for (int i = 0; i < notebooks.Count; i++)
                {
                    uniqueidslist.Add(notebooks[i].NotebookName, notebooks[i].cells.Select(y => y.UniqueId).ToList());
                }
                string uniqueidexists = "";
                foreach (var cell in NewUploadedNotebook.cells)
                {
                    if (cell.cell_type == "markdown")
                    {
                        //RULE 1-Check all existing notebooks if this UNIQUE ID already exists across any other notebook
                        if (uniqueidslist.Count() > 0 && cell.UniqueId!="")
                        {
                            uniqueidexists = uniqueidslist.Where(x => x.Value.Contains(cell.UniqueId)).Select(x => x.Key).FirstOrDefault();
                            if (uniqueidexists != null)
                            {
                                errorcount++;
                                System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Unique ID " + cell.UniqueId + " already exists in " + uniqueidexists + " Notebook\n");
                            }
                        }
                        //RULE 2- UNIQUE ID missing
                        if (cell.UniqueId == "")
                        {
                            errorcount++;
                            System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Unique ID Missing and is Mandatory\n");
                        }
                        //RULE 3-Minimum Content Missing for Comment
                        if(string.Join(",",cell.source).Length<250)
                        {
                            errorcount++;
                            System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Minimum Text Length required for Comment is 250 characters\n");
                        }
                        if(cell.source.Count>2)
                        {
                            //RULE 8-Second Line Check
                            if (!(cell.source[1] == "" || cell.source[1].Contains("ALLOWMERGEINTOANOTHERCELLFLAG-TRUE") || cell.source[1].Contains("ALLOWMERGEINTOANOTHERCELLFLAG-FALSE")))
                            {
                                errorcount++;
                                System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Second Line should be ALLOWMERGEINTOANOTHERCELLFLAG-TRUE or ALLOWMERGEINTOANOTHERCELLFLAG-FALSE or BLANK\n");
                            }
                            //RULE 9- Empty Line Check
                            if (!(cell.source[2] == "" || cell.source[2] == "\n"))
                            {
                                errorcount++;
                                System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Empty Line Mandatory after Header(UNIQUEID,FLAGS etc.\n");
                            }
                        }
                        
                    }
                    if(cell.cell_type=="code")
                    {
                        var cellcontent = string.Join(",", cell.source);
                        //RULE 4- Fixed Variables Error
                        if (cellcontent.Contains("##TABLENAME") || cellcontent.Contains("##COLUMNNAME") || cellcontent.Contains("##DATABASENAME"))
                        {
                            var contextcount = Regex.Matches(cellcontent, @"##[\w]+##", RegexOptions.Multiline);
                            var variableErrorCount = Regex.Matches(cellcontent, @"##[A-Z]+[0-9]+##", RegexOptions.Multiline);
                            if (variableErrorCount.Count < contextcount.Count)
                            {
                                errorcount++;
                                System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now +" "+ NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Expected ##TABLENAME1##,##COLUMNNAME1##, All variables require Number ID at the End.\n");
                            }
                        }
                        //RULE 5- Fixed Variables Warning
                        if (!(cellcontent.Contains("##TABLENAME") || cellcontent.Contains("##COLUMNNAME") || cellcontent.Contains("##DATABASENAME")))
                        {
                            warningcount++;
                            System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[WARNING] :  " + "Missing context sensitive Table Name , Column Name etc. in the code\n");
                        }
                        //RULE 6- MIN MAX Warning
                        if(!(cell.source.Count>5 && cell.source.Count<200))
                        {
                            warningcount++;
                            System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[WARNING] :  " + "Code should be minimum 5 lines and maximum 200 lines\n");
                        }
                        //RULE 7- Min Comment Blocks
                        if(NewUploadedNotebook.cells.Where(x=>x.cell_type=="markdown" && x.UniqueId==cell.UniqueId).ToList().Count==0)
                        {
                            errorcount++;
                            System.IO.File.AppendAllText(ValidationLogFilePath, DateTime.Now + " " + NewUploadedNotebook.NotebookName + "-" + NewUploadedNotebook.cells.IndexOf(cell) + "[ERROR] :  " + "Minimum One Comment block required before code block\n");
                        }
                        
                    }
                }
                if(errorcount==0)
                {
                    try
                    {
                        var file = files[0]; CloudBlockBlob temp; CloudBlockBlob blob = null;
                        if (cloudBlobContainer.ListBlobs().OfType<CloudBlockBlob>().Count() > 0)
                        {
                            blob = cloudBlobContainer.ListBlobs().OfType<CloudBlockBlob>().Where(b => b.Name == file.FileName).ToList()[0];
                        }
                        if (blob != null)
                        {
                            string name = blob.Uri.Segments.Last();
                            var destblob = cloudBlobContainer.GetBlockBlobReference(@"Backup\" + name.Split('.')[0] + "_" + version + ".ipynb");
                            destblob.StartCopy(blob);
                            blob.Delete();
                        }
                        temp = cloudBlobContainer.GetBlockBlobReference(file.FileName);
                        temp.Properties.ContentType = "text/csv";
                        temp.UploadText(notebooktemplate);
                        System.IO.File.WriteAllText(Server.MapPath(@"~\Content\Templates\") + file.FileName, notebooktemplate);
                    }
                    catch (Exception ex)
                    {
                    }
                }
                CloudBlockBlob logfile = cloudBlobContainer.GetBlockBlobReference(@"ValidationLogs\"+ValidationLogFile);
                logfile.Properties.ContentType = "text/csv";
                logfile.UploadFromFile(ValidationLogFilePath);
            }
            string virtualpath = "https://smdwstorage.blob.core.windows.net/notebooktemplates/ValidationLogs/" + ValidationLogFile;
            return Json(virtualpath, JsonRequestBehavior.AllowGet);
        }

       
        
        public Cell FormCell(List<string> source)
        {
            Metadata meta = new Metadata();
            Cell cell = new Cell();
            cell.source = source;
            cell.metadata = meta;
            cell.outputs = new List<object>();
            return cell;
        }
        
        
        public class ConfigData
        {
            public string TableName { get; set; }
            public bool Load { get; set; }
            public bool Profiling { get; set; }
            public string Derive { get; set; }
            public string Filter { get; set; }
            public string Transform { get; set; }
            public object Replace { get; set; }
            public object Assert { get; set; }
            public object Join { get; set; }
        }

        public class MLDataModel
        {
            public string TableName { get; set; }
            public List<ConfigData> ConfigData { get; set; }
        }
        
    }
}
