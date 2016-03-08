using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrossStampFunctional
{
    class Program
    {
        const string _sub = "00e7bb72-7725-4249-8e6b-0d2632b3bfc1";
        const string _ws = "eastuswebspace";
        const string _dummy = "suwatchet01";
        const string _site = "functionsuw200";
        const string _sf = "Default2";
        static string _storageAccount = "";
        static string _storageKey = "";

        const string _geoMasterCmd = @"c:\temp\geomaster\AntaresCmd.exe";
        const string _geoRegionCmd = @"c:\temp\georegion\AntaresCmd.exe";
        const string _blu1Cmd = @"c:\temp\blu1\AntaresCmd.exe";
        const string _blu2Cmd = @"c:\temp\blu2\AntaresCmd.exe";
        const string _blu1 = "blu1";
        const string _blu2 = "blu2";
        const string _full = "Full";
        const string _free = "Free";

        static void Main(string[] args)
        {
            try
            {
                var lines = File.ReadAllLines(@"\\iisdist\PublicLockBox\Antares\antfunctions.txt");
                _storageAccount = lines[0].Trim();
                _storageKey = lines[1].Trim();

                // Reset
                RunAndValidate(String.Empty,
                    _geoMasterCmd,
                    "DeleteWebSite {0} {1} {2}",
                    _sub, _ws, _site);
                RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("!" + _site, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
                RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
                RunAndValidate("!" + _site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

                // Warmup
                RunAndValidate(_dummy, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
                RunAndValidate(_dummy, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
                RunAndValidate("Completed successfully", _geoRegionCmd, "ListSiteStamps");

                // Create
                RunAndValidate(String.Format("{0}.kudu1.antares-test.windows-int.net", _site), 
                    _geoMasterCmd, 
                    "CreateFunctionSite {0} {1} {2} /storageAccount:{3} /storageKey:{4}",
                    _sub, _ws, _site, _storageAccount, _storageKey);

                // Notify full
                RunAndValidate("Completed successfully.", 
                    _geoRegionCmd, 
                    "Notify {0} {1} {2} {3} {4} {5}",
                    _blu1, _full, _sub, _ws, _site, _sf);
                RunAndValidate("StampName: blu1", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("StampName: blu2", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate(_site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

                // UpdateWebSiteConfig propagation
                RunAndValidate(String.Format("Configuration for website {0} has been updated.", _site), 
                    _geoMasterCmd, 
                    "UpdateWebSiteConfig {0} {1} {2} {3}",
                    _sub, _ws, _site, "/scmType:LocalGit");
                RunAndValidate("ScmType: LocalGit",
                    _blu2Cmd,
                    "GetWebSiteConfig {0} {1} {2}",
                    _sub, _ws, _site);

                // Notify free from blu2
                RunAndValidate("Completed successfully.",
                    _geoRegionCmd,
                    "Notify {0} {1} {2} {3} {4} {5}",
                    _blu2, _free, _sub, _ws, _site, _sf);
                RunAndValidate("!" + _blu2, _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("StampName: blu1", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("!" + _site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

                // Notify free from blu1
                RunAndValidate("Completed successfully.",
                    _geoRegionCmd,
                    "Notify {0} {1} {2} {3} {4} {5}",
                    _blu1, _free, _sub, _ws, _site, _sf);
                RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate(_site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);

                // Notify full
                RunAndValidate("Completed successfully.",
                    _geoRegionCmd,
                    "Notify {0} {1} {2} {3} {4} {5}",
                    _blu1, _full, _sub, _ws, _site, _sf);
                RunAndValidate("StampName: blu1", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("StampName: blu2", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate(_site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

                // Delete site
                RunAndValidate(String.Format("Website {0} has been deleted.", _site), 
                    _geoMasterCmd, 
                    "DeleteWebSite {0} {1} {2}",
                    _sub, _ws, _site);
                RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps");
                RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
                RunAndValidate("!" + _site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static bool RunAndValidate(string expected, string exe, string format, params object[] args)
        {
            Console.WriteLine();
            Console.WriteLine("Expected: {0}", expected);

            var notContains = expected.StartsWith("!");
            if (notContains)
            {
                expected = expected.Substring(1);
            }

            for (int i = 0; i < 60; ++i)
            {
                Console.WriteLine(DateTime.Now.ToString("o"));
                var output = Run(exe, format, args);
                if (notContains)
                {
                    if (output.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Console.WriteLine("Passed.");
                        return true;
                    }
                }
                else if (output.Contains(expected))
                {
                    Console.WriteLine("Passed.");
                    return true;
                }

                if (notContains)
                {
                    Console.WriteLine("Must NOT contains " + expected);
                }
                else
                {
                    Console.WriteLine("Must contains " + expected);
                }

                Thread.Sleep(1000);
            }

            throw new InvalidOperationException("Command did not return expected result!");
        }

        static string Run(string exe, string format, params object[] args)
        {
            Console.WriteLine("Run: {0} {1}", exe, String.Format(format, args));
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = String.Format(format, args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            proc.Start();

            var output = new StringBuilder();
            while (!proc.StandardOutput.EndOfStream)
            {
                var line = proc.StandardOutput.ReadLine();
                Console.WriteLine(line);
                output.AppendLine(line);
            }

            proc.WaitForExit();

            Console.WriteLine("ExitCode: {0}", proc.ExitCode);

            return output.ToString();
        }
    }
}
