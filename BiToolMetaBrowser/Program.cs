using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiToolMetaBrowser {
    internal class Program {
        private static int exportCount;
        private static string outputPath = string.Empty;
        private static string tableauFile = string.Empty;
        private static bool isExporting;
        private static readonly object Locker = new object();

        private static void Main(string[] args) {

            tableauFile = args[0];
            outputPath = args[1];

            Console.WriteLine($"Watching tableau file: {tableauFile}");
            Console.WriteLine($"Writing export to path:{outputPath}");

            // Create a new FileSystemWatcher and set its properties.
            var watcher = new FileSystemWatcher {
                Path = Path.GetDirectoryName(tableauFile),
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
                                                        | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = Path.GetFileName(tableauFile)
            };

            watcher.Changed += OnChanged;
            watcher.EnableRaisingEvents = true;

            //export on startup
            Export();
            Console.ReadLine();
        }

        private static bool GetIsExporting() {
            lock (Locker) {
                return isExporting;
            }
        }

        private static void SetIsExporting(bool x) {
            lock (Locker) {
                isExporting = x;
            }
        }

        private static async void OnChanged(object source, FileSystemEventArgs e) {
            //Tableau version 2018.1.8 (20181.18.1213.2251) saves three times back-to-back when user clicks the save button
            //this ensures that only one save is performed per half-second
            if (GetIsExporting())
                return;

            SetIsExporting(true);
            await Task.Delay(500);
            Export();
            SetIsExporting(false);
        }

        private static void Export() {
            exportCount++;
            Console.WriteLine($"Exporting #{exportCount} File: {tableauFile}");
            var wr = new WorkbookReader(tableauFile);
            wr.ExportToCsv(outputPath);
            Console.WriteLine($"Complete.");
        }
    }
}
