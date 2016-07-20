﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AntaresAzureDNS;

namespace CrossStampFunctional
{
    class Program
    {
        const string _sub = "00e7bb72-7725-4249-8e6b-0d2632b3bfc1";
        const string _ws = "eastuswebspace";
        const string _dummy = "suwatchet01";
        const string _site = "functionsuw200";
        const string _sf = "EastUSPlan";
        static string _storageAccount = "";
        static string _storageKey = "";
        static IList<string> _workers = null;
        static string _publishingUserName = null;
        static string _publishingPassword = null;
        static string _validationKey = null;
        static string _decryptionKey = null;

        const string _geoMasterCmd = @"c:\temp\geomaster\AntaresCmd.exe";
        const string _geoRegionCmd = @"c:\temp\georegion\AntaresCmd.exe";
        const string _blu1Cmd = @"c:\temp\blu1\AntaresCmd.exe";
        const string _blu2Cmd = @"c:\temp\blu2\AntaresCmd.exe";
        const string _blu1 = "blu1";
        const string _blu2 = "blu2";
        const string _full = "Full";
        const string _free = "Free";
        const string _dnsSuffix = "kudu1.antares-test.windows-int.net";
        const string _blu1HostName = "blu1.api.kudu1.antares-test.windows-int.net";
        const string _blu2HostName = "blu2.api.kudu1.antares-test.windows-int.net";
        static string _blu1IPAddress;
        static string _blu2IPAddress;

        const string _clientCertPfxFile = @"\\iisdist\PublicLockBox\Antares\RdfeTestClientCert2016.pfx";
        const string _clientCertPwdFile = @"\\iisdist\PublicLockBox\Antares\RdfeTestClientCert2016.pfx.txt";
        const string _clientCertThumbprintFile = @"\\iisdist\PublicLockBox\Antares\RdfeTestClientCert2016.tp.txt";
        static string _clientCertPwd = File.ReadAllText(_clientCertPwdFile).Trim();
        static string _clientCertThumbprint = File.ReadAllText(_clientCertThumbprintFile).Trim().ToUpperInvariant();

        static List<Exception> _dnsExceptions = new List<Exception>();

        static void Main(string[] args)
        {
            try
            {
                // initially no timer trigger
                Initialize();

                CreateDummySite();

                CreateFunctionSite();

                ValidateHotBackup();

                HttpForwardTest();

                // TODO: machine key test

                NotifyFullTest();

                ValidateConfigPropagation();

                ValidateCertificatesPropagation();

                ValidateTimerTriggerPropagation();

                NotifyFreeTest();

                NotifyFullTest();

                DeleteFunctionSite();

                foreach (var dnsException in _dnsExceptions)
                {
                    Console.WriteLine(dnsException);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static void Initialize()
        {
            var lines = File.ReadAllLines(@"\\iisdist\PublicLockBox\Antares\antfunctions.txt");
            _storageAccount = lines[0].Trim();
            _storageKey = lines[1].Trim();

            InitializeStampIPs();

            EnableAllWorkers();

            DeleteAllCertificates();

            // Reset
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                "DeleteWebSite {0} {1} {2} /deleteEmptyServerFarm",
                _sub, _ws, _site);
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                "DeleteWebSite {0} {1} {2}",
                _sub, _ws, _dummy);
            RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("!" + _dummy, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);
        }

        static void InitializeStampIPs()
        {
            _blu1IPAddress = Dns.GetHostEntry(_blu1HostName).AddressList[0].ToString();
            _blu2IPAddress = Dns.GetHostEntry(_blu2HostName).AddressList[0].ToString();
        }

        static void ValidateDnsHostEntry(string hostName, params string[] ipAddresses)
        {
            foreach (var ipAddress in ipAddresses)
            {
                var success = false;
                var content = string.Empty;
                Console.WriteLine();
                for (int i = 0; i < 24 && !success; ++i)
                {
                    Console.WriteLine("ValidateDns: {0}, {1}", hostName, ipAddress);
                    Console.WriteLine(DateTime.Now.ToString("o"));

                    var addresses = string.Join(",", GetIpAddresses(hostName));
                    Console.WriteLine("Dns: {0}, {1}", hostName, addresses);
                    var expect = ipAddress.Trim('!');
                    success = addresses.Contains(expect) == !ipAddress.StartsWith("!");

                    //var digUrl = string.Format("http://dig.jsondns.org/IN/{0}/A", hostName);
                    //using (var client = new HttpClient())
                    //{
                    //    using (var response = client.GetAsync(digUrl).Result)
                    //    {
                    //        content = "Dig status: " + response.StatusCode;
                    //        Console.WriteLine(content);
                    //        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                    //        {
                    //            content = response.Content.ReadAsStringAsync().Result;
                    //            var expect = ipAddress.Trim('!');
                    //            success = content.Contains("\"" + expect + "\"") == !ipAddress.StartsWith("!");
                    //        }
                    //    }
                    //}
                }

                if (success)
                {
                    Console.WriteLine("Passed.");
                }
                else
                {
                    try
                    {
                        throw new InvalidOperationException(String.Format(DateTime.Now.ToString("o") + ", ValidateDns: {0}, {1}", ipAddress, hostName));
                    }
                    catch (Exception ex)
                    {
                        _dnsExceptions.Add(ex);      
                    }
                }
            }
        }

        static string[] GetIpAddresses(string hostName)
        {
            var addresses = new List<string>();
            GetIpAddresses(hostName, addresses);
            return addresses.ToArray(); 
        }

        static void GetIpAddresses(string hostName, List<string> addresses)
        {
            if (hostName.EndsWith(_dnsSuffix))
            {
                foreach (var name in DNSHelper.ListAllCNameRecords(hostName))
                {
                    GetIpAddresses(name.TrimEnd('.'), addresses);
                }

                foreach (var aRecord in DNSHelper.ListAllARecords(hostName))
                {
                    addresses.Add(aRecord);
                }
            }
            else
            {
                var entry = Dns.GetHostEntry(hostName);
                addresses.AddRange(entry.AddressList.Select(ip => ip.ToString()));
            }
        }

        //static string GetIpAddresses(string hostName)
        //{
        //    try
        //    {
        //        var entry = Dns.GetHostEntry(hostName);
        //        return string.Join(",", entry.AddressList.Select(ip => "[" + ip.ToString() + "]"));
        //    }
        //    catch (Exception ex)
        //    {
        //        return ex.Message;
        //    }
        //}

        static void CreateDummySite()
        {
            // Warmup
            RunAndValidate(String.Format("{0}.{1}", _dummy, _dnsSuffix),
                _geoMasterCmd,
                "CreateWebSite {0} {1} {2}",
                _sub, _ws, _dummy);
            RunAndValidate(_dummy, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate(_dummy, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("Completed successfully", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
        }

        static void CreateFunctionSite()
        {
            // CreateFunctionSite
            RunAndValidate(String.Format("{0}.{1}", _site, _dnsSuffix),
                _geoMasterCmd,
                "CreateFunctionSite {0} {1} {2} /storageAccount:{3} /storageKey:{4} /appSettings:WEBSITE_LOAD_CERTIFICATES=*",
                _sub, _ws, _site, _storageAccount, _storageKey);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);

            // initially no timer trigger
            DeleteTimerTrigger();

            RunAndValidate("SyncWebSiteTriggers Response: OK",
                _geoMasterCmd,
                "SyncWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("Triggers: [{\"type\":\"",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);

            // Get publishing cred
            EnsurePublishingCred();

            // Get MachineKey
            EnsureMachineKey();
        }

        static void EnsurePublishingCred()
        {
            if (_publishingUserName == null || _publishingPassword == null)
            {
                Console.WriteLine(DateTime.Now.ToString("o"));
                var output = Run(_blu1Cmd, "GetWebSiteConfig {0} {1} {2}", _sub, _ws, _site);

                using (var reader = new StringReader(output))
                {
                    string publishingUserName = null;
                    string publishingPassword = null;
                    while (publishingUserName == null || publishingPassword == null)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            break;
                        }

                        // PublishingUsername: $functionsuw200
                        // PublishingPassword: WhMBqp0mjsYKCem6sQwsYrDce9xo0x86jGoeGmud1JkJxbNYDvujZvDyYCCA\
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            if (parts[0] == "PublishingUsername:")
                            {
                                publishingUserName = parts[1];
                            }
                            else if (parts[0] == "PublishingPassword:")
                            {
                                publishingPassword = parts[1];
                            }
                        }
                    }

                    if (publishingUserName == null || publishingPassword == null)
                    {
                        throw new InvalidOperationException("no publishing cred found.  " + output);
                    }

                    _publishingUserName = publishingUserName;
                    _publishingPassword = publishingPassword;
                }
            }
        }

        static void EnsureMachineKey()
        {
            if (_validationKey == null || _decryptionKey == null)
            {
                Console.WriteLine(DateTime.Now.ToString("o"));
                var output = Run(_blu1Cmd, "GetWebSiteConfig {0} {1} {2}", _sub, _ws, _site);

                using (var reader = new StringReader(output))
                {
                    string validationKey = null;
                    string decryptionKey = null;
                    while (validationKey == null || decryptionKey == null)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            break;
                        }

                        // ValidationKey: 9463CD095F3AA2C2EE0663B102CED9C61B40A06D0C3C4ADDD616AA3EA6614064
                        // Decryption: AES
                        // DecryptionKey: 7BE95A7E5B94976384F42C9E9CC34027A7CC00E03C3CC8C960C0D8631412C076
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            if (parts[0] == "ValidationKey:")
                            {
                                validationKey = parts[1];
                            }
                            else if (parts[0] == "DecryptionKey:")
                            {
                                decryptionKey = parts[1];
                            }
                        }
                    }

                    if (validationKey == null || decryptionKey == null)
                    {
                        throw new InvalidOperationException("no machine key found.  " + output);
                    }

                    _validationKey = validationKey;
                    _decryptionKey = decryptionKey;
                }
            }
        }

        static void ValidateTimerTriggerPropagation()
        {
            AddTimerTrigger();

            RunAndValidate("SyncWebSiteTriggers Response: OK",
                _geoMasterCmd,
                "SyncWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("\"type\":\"timerTrigger",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("\"type\":\"timerTrigger",
                _blu2Cmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);
        }

        static void ValidateHotBackup()
        {
            // Check if hot back up
            RunAndValidate("StampName: " + _blu1, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("StampName: " + _blu2, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("Idle: True", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate(_site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("State: Running", _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
            HttpGet(new Uri(String.Format("http://{0}", _blu1HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.NoContent);
            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            // always serve incoming request to slave stamp
            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.NoContent);

            // Ensure same publishing cred
            RunAndValidate("PublishingPassword: " + _publishingPassword, 
                _blu2Cmd, 
                "GetWebSiteConfig {0} {1} {2}", 
                _sub, _ws, _site);

            // Ensure same machine key
            RunAndValidate("ValidationKey: " + _validationKey,
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("DecryptionKey: " + _decryptionKey,
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);
        }

        static void HttpForwardTest()
        {
            DisableAllWorkers();

            // wait for host cache to stale
            Thread.Sleep(30000);

            HttpGet(new Uri(String.Format("http://{0}.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.NoContent);

            HttpGet(new Uri(String.Format("https://{0}.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.NoContent);

            HttpGet(new Uri(String.Format("http://{0}.scm.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.Redirect);

            HttpGet(new Uri(String.Format("https://{0}.scm.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.Unauthorized);

            HttpGet(new Uri(String.Format("https://{0}.scm.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.OK,
                _publishingUserName,
                _publishingPassword);

            EnableAllWorkers();
        }

        static void DisableAllWorkers()
        {
            foreach (var worker in GetAllWorkers())
            {
                SetWorkerState(worker, enabled: false);
            }
        }

        static void EnableAllWorkers()
        {
            foreach (var worker in GetAllWorkers())
            {
                SetWorkerState(worker, enabled: true);
            }
        }

        static void SetWorkerState(string worker, bool enabled)
        {
            RunAndValidate(String.Format("Web worker {0} has been updated", worker),
                _blu1Cmd,
                "SetWebWorkerState WebSites {0} /enabled:{1}", worker, enabled ? '1' : '0'); 
        }

        static IList<string> GetAllWorkers()
        {
            if (_workers == null)
            {
                Console.WriteLine(DateTime.Now.ToString("o"));
                var output = Run(_blu1Cmd, "ListWebWorkers WebSites");
                var workers = new List<string>();
                using (var reader = new StringReader(output))
                {
                    while (true)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            break;
                        }

                        IPAddress address;
                        if (IPAddress.TryParse(line.Trim(), out address))
                        {
                            workers.Add(address.ToString());
                        }
                    }
                }

                if (!workers.Any())
                {
                    throw new InvalidOperationException("no worker found.  " + output);
                }

                _workers = workers;
            }

            return _workers;
        }

        static void NotifyFullTest()
        {
            // Notify full
            RunAndValidate("Completed successfully.",
                _geoRegionCmd,
                "Notify {0} {1} {2} {3} {4} {5}",
                _blu1, _full, _sub, _ws, _site, _sf);
            RunAndValidate("StampName: blu1", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("StampName: blu2", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate(_site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("!Idle: True", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("State: Running", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.NoContent);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
        }

        static void ValidateConfigPropagation()
        {
            // compensate for clock skew
            Thread.Sleep(60000);

            // UpdateWebSiteConfig propagation
            RunAndValidate(String.Format("Configuration for website {0} has been updated.", _site),
                _geoMasterCmd,
                "UpdateWebSiteConfig {0} {1} {2} {3}",
                _sub, _ws, _site, "/scmType:LocalGit");
            RunAndValidate("ScmType: LocalGit",
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);

            // Ensure same publishing cred
            RunAndValidate("PublishingPassword: " + _publishingPassword,
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);

            // Ensure same machine key
            RunAndValidate("ValidationKey: " + _validationKey,
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("DecryptionKey: " + _decryptionKey,
                _blu2Cmd,
                "GetWebSiteConfig {0} {1} {2}",
                _sub, _ws, _site);
        }

        static void DeleteAllCertificates()
        {
            foreach (var thumbprint in GetAllCertificates())
            {
                RunAndValidate("Certificate was deleted.",
                    _geoMasterCmd,
                    "DeleteCertificate {0} {1} {2}",
                    _sub, _ws, thumbprint);
            }
        }

        static IList<string> GetAllCertificates()
        {
            Console.WriteLine(DateTime.Now.ToString("o"));
            var output = Run(_geoMasterCmd, "GetCertificates {0} {1}", _sub, _ws);
            var thumbprints = new List<string>();
            using (var reader = new StringReader(output))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    var index = line.IndexOf("Thumbprint: ");
                    if (index >= 0)
                    {
                        thumbprints.Add(line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Last().Trim());
                    }
                }
            }

            return thumbprints;
        }

        static void ValidateCertificatesPropagation()
        {
            // compensate for clock skew
            Thread.Sleep(60000);

            // AddCertificates propagation
            RunAndValidate(String.Format("Certficates were added to the webspace", _site),
                _geoMasterCmd,
                "AddCertificates {0} {1} {2} {3}",
                _sub, _ws, _clientCertPfxFile, _clientCertPwd);
            RunAndValidate("Thumbprint: " + _clientCertThumbprint,
                _blu2Cmd,
                "GetCertificates {0} {1}",
                _sub, _ws);

            // Hit the site
            //HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTrigger?name=foo", _blu1HostName)),
            //    String.Format("{0}.{1}", _site, _dnsSuffix),
            //    HttpStatusCode.OK,
            //    expectedContent: _clientCertThumbprint);
            //HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTrigger?name=foo", _blu2HostName)),
            //    String.Format("{0}.{1}", _site, _dnsSuffix),
            //    HttpStatusCode.OK,
            //    expectedContent: _clientCertThumbprint);

            // DeleteCertificate propagation
            RunAndValidate("Certificate was deleted.",
                _geoMasterCmd,
                "DeleteCertificate {0} {1} {2}",
                _sub, _ws, _clientCertThumbprint);
            RunAndValidate("!Thumbprint:",
                _blu2Cmd,
                "GetCertificates {0} {1}",
                _sub, _ws);

            // Hit the site
            //HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTrigger?name=foo", _blu1HostName)),
            //    String.Format("{0}.{1}", _site, _dnsSuffix),
            //    HttpStatusCode.OK,
            //    expectedContent: "!" + _clientCertThumbprint);
            //HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTrigger?name=foo", _blu2HostName)),
            //    String.Format("{0}.{1}", _site, _dnsSuffix),
            //    HttpStatusCode.OK,
            //    expectedContent: "!" + _clientCertThumbprint);
        }

        static void NotifyFreeTest()
        {
            // Notify free from blu2
            RunAndValidate("Completed successfully.",
                _geoRegionCmd,
                "Notify {0} {1} {2} {3} {4} {5}",
                _blu2, _free, _sub, _ws, _site, _sf);
            RunAndValidate("StampName: " + _blu1, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("StampName: " + _blu2, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("Idle: True", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate(_site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("State: Running", _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
            HttpGet(new Uri(String.Format("http://{0}", _blu1HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.NoContent);

            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            // always serve incoming request to slave stamp
            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.NoContent);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
        }

        static void DeleteFunctionSite()
        {
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                "DeleteWebSite {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("!" + _site, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu2Cmd, 240, "ListWebSites {0} {1}", _sub, _ws);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), "!" + _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), "!" + _blu1IPAddress, "!" + _blu2IPAddress);
        }

        static bool HasTimerTrigger()
        {
            var request = GetTimerTriggerRequest();
            request.Method = "GET";

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", HasTimerTrigger ");
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Console.WriteLine("response " + response.StatusCode);
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Not Found"))
                {
                    Console.WriteLine("response NotFound");
                    return false;
                }

                throw;
            }
        }

        static void DeleteTimerTrigger()
        {
            var request = GetTimerTriggerRequest();
            request.Method = "DELETE";
            request.Headers.Add("If-Match", "*");

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", DeleteTimerTrigger ");
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Console.WriteLine("response " + response.StatusCode);
                }
            }
            catch (WebException ex)
            {
                if (!ex.Message.Contains("(404)"))
                {
                    throw;
                }
            }
        }

        static void AddTimerTrigger()
        {
            const string TimerTriggerFile = @"\\iisdist\PublicLockBox\Antares\SampleTimerTrigger_function.json";

            var request = GetTimerTriggerRequest();
            request.Method = "PUT";
            request.Headers.Add("If-Match", "*");
            request.ContentType = "application/json";

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", AddTimerTrigger ");
            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(File.ReadAllText(TimerTriggerFile));
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                Console.WriteLine("response " + response.StatusCode);
            }
        }

        static HttpWebRequest GetTimerTriggerRequest()
        {
            var timerTriggerUrl = String.Format("https://{0}.scm.{1}/vfs/site/wwwroot/SampleTimerTrigger/function.json", _site, _dnsSuffix);
            var request = (HttpWebRequest)WebRequest.Create(timerTriggerUrl);
            request.Credentials = new NetworkCredential("auxtm230", "iis6!dfu");
            return request;
        }

        static bool RunAndValidate(string expected, string exe, string format, params object[] args)
        {
            return RunAndValidate(expected, exe, 60, format, args);
        }

        static bool RunAndValidate(string expected, string exe, int numRetries, string format, params object[] args)
        {
            Console.WriteLine();
            Console.WriteLine("Expected: {0}", expected);

            var notContains = expected.StartsWith("!");
            if (notContains)
            {
                expected = expected.Substring(1);
            }

            for (int i = 0; i < numRetries; ++i)
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

                if ((i + 1) >= numRetries)
                {
                    Console.WriteLine(output);
                    break;
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
                //Console.WriteLine(line);
                output.AppendLine(line);
            }

            proc.WaitForExit();

            Console.WriteLine("ExitCode: {0}", proc.ExitCode);

            return output.ToString();
        }

        static void HttpGet(Uri uri, string host, HttpStatusCode expected, string userName = null, string password = null, string expectedContent = null)
        {
            for (int i = 0; i < 60; ++i)
            {
                Console.WriteLine(DateTime.Now.ToString("o"));
                Console.WriteLine("HttpGet: {0}", uri);
                Console.WriteLine("Host: {0}", host);
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    if (host != null)
                    {
                        client.DefaultRequestHeaders.Host = host;
                    }

                    if (userName != null && password != null)
                    {
                        var byteArray = Encoding.ASCII.GetBytes(String.Format("{0}:{1}", userName, password));
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }

                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("CrossStampFunctional", "1.0.0.0")));

                    try
                    {
                        using (var response = client.GetAsync(uri).Result)
                        {
                            if (response.StatusCode == expected && IsResponseContentMatch(expectedContent, response))
                            {
                                Console.WriteLine("HttpStatus: {0} == {1}", response.StatusCode, expected);
                                Console.WriteLine("Passed.");
                                Console.WriteLine();
                                return;
                            }

                            Console.WriteLine("HttpStatus: {0} != {1}", response.StatusCode, expected);
                            Console.WriteLine("X-Powered-By: {0}", string.Join("; ", response.Headers.GetValues("X-Powered-By").ToArray()));
                            Console.WriteLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception: {0}", ex);
                        Console.WriteLine();
                    }
                }

                Thread.Sleep(1000);
            }

            throw new InvalidOperationException("Command did not return expected result!");
        }

        static bool IsResponseContentMatch(string expected, HttpResponseMessage response)
        {
            if (String.IsNullOrEmpty(expected))
            {
                return true;
            }

            var negate = expected.StartsWith("!");
            if (negate)
            {
                expected = expected.TrimStart('!');
            }

            var actual = response.Content.ReadAsStringAsync().Result;
            if (negate)
            {
                return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0;
            }
            else
            {
                return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}