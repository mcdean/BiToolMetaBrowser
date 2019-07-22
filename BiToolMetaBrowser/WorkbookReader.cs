using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace BiToolMetaBrowser {
    public class WorkbookReader {
        private const string D = ";";
        private const string FilePrefix = "tmb_";
        public string WorkbookFile { get; set; }
        public Dictionary<string, string> FieldNameMap { get; set; }
        public Dictionary<string, List<string>> NativeFieldUsageInCalulatedFields { get; set; }
        //TODO: Combine these dictionaries with a CalculatedFieldInfo class
        public Dictionary<string, string> CalculatedFieldDefinitions { get; set; }
        public Dictionary<string, List<string>> CalculatedFieldUsageInCalculatedFields { get; set; }
        public Dictionary<string, List<string>> CalculatedFieldUsageInWorksheets { get; set; }
        //TODO: capture some additional info on worksheets and create a worksheetInfo class
        public Dictionary<string, string> Worksheets { get; set; }
        public Dictionary<string, List<string>> Dashboards { get; set; }



        public WorkbookReader(string workbookFile) {

            var doc = new XmlDocument();
            doc.Load(workbookFile);

            CalculatedFieldDefinitions = new Dictionary<string, string>();
            FieldNameMap = new Dictionary<string, string>();
            CalculatedFieldUsageInCalculatedFields = new Dictionary<string, List<string>>();
            CalculatedFieldUsageInWorksheets = new Dictionary<string, List<string>>();
            NativeFieldUsageInCalulatedFields = new Dictionary<string, List<string>>();
            Worksheets = new Dictionary<string, string>();
            Dashboards = new Dictionary<string, List<string>>();

            var columnNodes = doc?.DocumentElement?.SelectNodes("/workbook/datasources/datasource/column");

            if (columnNodes == null) {
                throw new ArgumentException("Tableau file's xml format is unexpected.");
            }

            foreach (XmlNode colNode in columnNodes) {

                if (colNode.Attributes == null) {
                    throw new ArgumentException(
                        "Tableau file's xml format is unexpected, calcNode is missing attributes.");
                }

                var name = colNode.Attributes["name"].Value;
                var calcChild = colNode.SelectSingleNode("calculation");
                var caption = colNode.Attributes["caption"]?.Value;
                var isParam = colNode.Attributes["param-domain-type"] != null;

                //columns may have no caption
                if (caption != null) {
                    FieldNameMap.Add(name, $"[{caption}]");
                }

                //is calculated field? Also ignore the "Number of Records" calculation (has no caption)
                if (calcChild != null && caption != null && !isParam) {

                    if (calcChild?.Attributes == null) {
                        throw new ArgumentException(
                            "Tableau file's xml format is unexpected, calculation child tag of measure is missing.");
                    }

                    var formula = calcChild.Attributes["formula"].Value;
                    //TODO: initialize in a CalculatedFieldInfo class, instead
                    CalculatedFieldDefinitions.Add(name, formula);
                    CalculatedFieldUsageInCalculatedFields.Add(name, new List<string>());
                    CalculatedFieldUsageInWorksheets.Add(name, new List<string>());
                }
                else {
                    //TODO: Allow duplicate field names across different data sources.
                    //TODO: For now, these are dumped to file but not consumed by the
                    //TODO: meta browser workbook in any of its sheets.
                    if (!NativeFieldUsageInCalulatedFields.ContainsKey(name)) {
                        NativeFieldUsageInCalulatedFields.Add(name, new List<string>());
                    }
                }
            }

            //determine usage of calculated fields in other calculated fields
            foreach (var cf in CalculatedFieldDefinitions.Keys) {

                //loop 
                foreach (var cfCompare in CalculatedFieldDefinitions.Keys) {
                    if (cf == cfCompare) continue;

                    //if definition contains cfCompare
                    if (CalculatedFieldDefinitions[cfCompare].Contains(cf)) {
                        CalculatedFieldUsageInCalculatedFields[cf].Add(cfCompare);
                    }
                }
            }

            //determine usage of native fields in other calculated fields
            foreach (var nf in NativeFieldUsageInCalulatedFields.Keys) {

                //loop 
                foreach (var cfCompare in CalculatedFieldDefinitions.Keys) {
                    if (nf == cfCompare) continue;

                    //if definition contains cfCompare
                    if (CalculatedFieldDefinitions[cfCompare].Contains(nf)) {
                        NativeFieldUsageInCalulatedFields[nf].Add(cfCompare);
                    }
                }
            }

            var worksheetNodes = doc?.DocumentElement?.SelectNodes("/workbook/worksheets/worksheet");

            if (worksheetNodes != null) {

                //save the full list of worksheets
                foreach (XmlNode ws in worksheetNodes) {
                    var wsName = ws.Attributes?["name"].Value;
                    if (wsName != null) {
                        Worksheets.Add(wsName, wsName);
                    }
                }

                //find the usage of calculated fields in worksheets
                foreach (var cf in CalculatedFieldDefinitions.Keys) {

                    foreach (XmlNode ws in worksheetNodes) {

                        //compare with each column in the worksheet
                        var wsColumnNodes = ws.SelectNodes("table/view/datasource-dependencies/column");

                        if (wsColumnNodes == null) continue;

                        foreach (XmlNode wsColumn in wsColumnNodes) {
                            if (wsColumn.Attributes == null) continue;

                            if (cf == wsColumn.Attributes["name"].Value) {
                                CalculatedFieldUsageInWorksheets[cf].Add(ws.Attributes?["name"].Value);
                            }
                        }
                    }
                }
            }

            var dashboardNodes = doc?.DocumentElement?.SelectNodes("/workbook/dashboards/dashboard");
            if (dashboardNodes != null) {
                //save the full list of worksheets
                foreach (XmlNode dashboardNode in dashboardNodes) {
                    var dashboardName = dashboardNode.Attributes?["name"].Value;
                    if (dashboardName != null) {
                        var zonesNode = dashboardNode.SelectSingleNode("zones");
                        var worksheetsInZone = GetWorkSheetsInZones(zonesNode);
                        Dashboards.Add(dashboardName, worksheetsInZone);
                    }
                }
            }



            //resolve the "name" to "caption" inside the formulas
            var keys = CalculatedFieldDefinitions.Keys.ToList();
            foreach (var cf in keys) {

                var formula = CalculatedFieldDefinitions[cf];

                //use simple approach to replacement, try replacing all calc names with the caption
                foreach (var cfToResolve in FieldNameMap.Keys) {
                    var caption = FieldNameMap[cfToResolve];
                    formula = formula.Replace(cfToResolve, caption);
                }

                CalculatedFieldDefinitions[cf] = formula;
            }
        }

        List<string> GetWorkSheetsInZones(XmlNode zonesNode) {

            var worksheets = new List<string>();
            var zoneList = zonesNode?.SelectNodes("zone");
            if (zoneList == null) {
                return worksheets;
            }

            foreach (XmlNode zoneNode in zoneList) {
                AddWorkSheetsInZone(zoneNode, worksheets);
            }

            return worksheets;
        }

        void AddWorkSheetsInZone(XmlNode zone, List<string> worksheets) {
            //if zone is a worksheet zone, save the name of it
            var zName = zone.Attributes?["name"]?.Value;
            if (zName != null) {
                worksheets.Add(zName);
            }
            else {
                //recurse on children zones
                var zoneList = zone.SelectNodes("zone");
                if (zoneList == null) {
                    return;
                }

                foreach (XmlNode zoneNode in zoneList) {
                    AddWorkSheetsInZone(zoneNode, worksheets);
                }
            }
        }

        public void ExportToCsv(string path) {

            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            ExportDashboards(path);
            ExportWorksheets(path);
            ExportCalcFieldMap(path);
            ExportCalcFieldDefs(path);
            ExportCalcFieldUsageInCalc(path);
            ExportCalcFieldUsageInWorksheets(path);
            ExportNativeFieldUsageInCalcs(path);
        }

        private void ExportDashboards(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"Dashboard{D}Worksheet");

            foreach (var d in Dashboards.Keys) {
                //include empty dashboards
                if (Dashboards[d].Count == 0) {
                    sb.AppendLine($"{d}{D}");
                    continue;
                }
                foreach (var ws in Dashboards[d]) {
                    sb.AppendLine($"{d}{D}{ws}");
                }
            }

            File.WriteAllText($"{path}/{FilePrefix}Dashboards.csv", sb.ToString());
        }

        private void ExportWorksheets(string path) {
            var sb = new StringBuilder();
            sb.AppendLine("Worksheet");
            foreach (var ws in Worksheets.Keys) {
                sb.AppendLine(ws);
            }

            File.WriteAllText($"{path}/{FilePrefix}Worksheets.csv", sb.ToString());
        }

        private void ExportCalcFieldDefs(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"FieldName{D}Definition");
            foreach (var k in CalculatedFieldDefinitions.Keys) {
                sb.AppendLine($"{k}{D}\"{CalculatedFieldDefinitions[k]}\"");
            }

            File.WriteAllText($"{path}/{FilePrefix}CalcFieldDefs.csv", sb.ToString());
        }

        private void ExportCalcFieldMap(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"FieldName{D}FriendlyName");
            foreach (var k in FieldNameMap.Keys) {
                sb.AppendLine($"{k}{D}{FieldNameMap[k]}");
            }

            File.WriteAllText($"{path}/{FilePrefix}FieldMap.csv", sb.ToString());
        }

        private void ExportCalcFieldUsageInCalc(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"FieldName{D}CalculatedFieldCaller");
            foreach (var k in CalculatedFieldUsageInCalculatedFields.Keys) {
                foreach (var fieldUsage in CalculatedFieldUsageInCalculatedFields[k]) {
                    sb.AppendLine($"{k}{D}{fieldUsage}");
                }
            }

            File.WriteAllText($"{path}/{FilePrefix}CalcFieldUsageInCalcs.csv", sb.ToString());
        }

        private void ExportCalcFieldUsageInWorksheets(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"FieldName{D}Worksheet");
            foreach (var k in CalculatedFieldUsageInWorksheets.Keys) {
                foreach (var fieldUsage in CalculatedFieldUsageInWorksheets[k]) {
                    sb.AppendLine($"{k}{D}{fieldUsage}");
                }
            }

            File.WriteAllText($"{path}/{FilePrefix}CalcFieldUsageInWorksheets.csv", sb.ToString());
        }

        private void ExportNativeFieldUsageInCalcs(string path) {
            var sb = new StringBuilder();
            sb.AppendLine($"NativeFieldName{D}CalculatedField");
            foreach (var k in NativeFieldUsageInCalulatedFields.Keys) {
                foreach (var fieldUsage in NativeFieldUsageInCalulatedFields[k]) {
                    sb.AppendLine($"{k}{D}{fieldUsage}");
                }
            }

            File.WriteAllText($"{path}/{FilePrefix}NativeFieldUsageInCalcs.csv", sb.ToString());
        }
    }
}
