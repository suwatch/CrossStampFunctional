using System;
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
        const string _siteSlot = "functionslot200";
        const string _slotName = "Staging";
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

        const string _clientCertPfxFile = @"c:\temp\RdfeTestClientCert2016.pfx";
        const string _clientCertPwdFile = @"c:\temp\RdfeTestClientCert2016.pfx.txt";
        const string _clientCertThumbprintFile = @"c:\temp\RdfeTestClientCert2016.tp.txt";
        static string _clientCertPwd = File.ReadAllText(_clientCertPwdFile).Trim();
        static string _clientCertThumbprint = File.ReadAllText(_clientCertThumbprintFile).Trim().ToUpperInvariant();

        static int _requestId = 10000000;

        static int Main(string[] args)
        {
            try
            {
                // initially no timer trigger
                Initialize();

                CreateDummySite();

                CreateFunctionSite();

                ValidateHotBackup();

                HttpForwardTest();

                NotifyFullTest();

                ValidateConfigPropagation();

                ValidateServerFarmPropagation();

                ValidateCertificatesPropagation();

                ValidateTimerTriggerPropagation();

                NotifyFreeTest();

                NotifyFullTest();

                SlotCreateFunctionSite();

                SlotSwapTest_1();

                SlotSwapTest_2();

                DeleteFunctionSite();

                Console.WriteLine("{0} Summary results: passed", DateTime.Now.ToString("o"));

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} Summary results: failed {1}", DateTime.Now.ToString("o"), ex);

                return -1;
            }
        }

        static void Initialize()
        {
            var lines = File.ReadAllLines(@"c:\temp\antfunctions.txt");
            _storageAccount = lines[0].Trim();
            _storageKey = lines[1].Trim();

            InitializeStampIPs();

            EnableAllWorkers();

            DeleteAllCertificates();

            // Reset
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                1,
                "DeleteWebSite {0} {1} {2} /deleteEmptyServerFarm /deleteAllSlots",
                _sub, _ws, _siteSlot);
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                1,
                "DeleteWebSite {0} {1} {2} /deleteEmptyServerFarm  /deleteAllSlots",
                _sub, _ws, _site);
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                1,
                "DeleteWebSite {0} {1} {2}",
                _sub, _ws, _dummy);
            RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("!" + _dummy, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("!" + _siteSlot, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _siteSlot, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _siteSlot, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            //RunAndValidate("!" + _sf, _geoMasterCmd, "ListServerFarms {0} {1}", _sub, _ws);
            //RunAndValidate("!" + _sf, _blu1Cmd, "ListServerFarms {0} {1}", _sub, _ws);
            //RunAndValidate("!" + _sf, _blu2Cmd, "ListServerFarms {0} {1}", _sub, _ws);
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

                    if (!success)
                    {
                        Thread.Sleep(5000);
                    }

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
                    throw new InvalidOperationException(String.Format(DateTime.Now.ToString("o") + ", ValidateDns: {0}, {1}", ipAddress, hostName));
                    //try
                    //{
                    //    throw new InvalidOperationException(String.Format(DateTime.Now.ToString("o") + ", ValidateDns: {0}, {1}", ipAddress, hostName));
                    //}
                    //catch (Exception ex)
                    //{
                    //    _dnsExceptions.Add(ex);      
                    //}
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
                1,
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
                1,
                "CreateFunctionSite {0} {1} {2} /storageAccount:{3} /storageKey:{4} /appSettings:WEBSITE_LOAD_CERTIFICATES=*,FUNCTIONS_EXTENSION_VERSION=~0.8",
                _sub, _ws, _site, _storageAccount, _storageKey);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);

            // initially no timer trigger
            RetryHelper(() => DeleteTimerTrigger());

            RunAndValidate("SyncWebSiteTriggers Response: OK",
                _geoMasterCmd,
                "SyncWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _site);
            RunAndValidate("Triggers: [{\"",
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
            // add timer trigger
            RetryHelper(() => AddTimerTrigger());

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
            RunAndValidate("HomeStamp: " + _blu1, _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
            HttpGet(new Uri(String.Format("http://{0}", _blu1HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.OK);
            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
            RunAndValidate("HomeStamp: " + _blu1, _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            // always serve incoming request to slave stamp
            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.OK);

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
            Console.WriteLine("{0} Sleep 60s", DateTime.Now.ToString("o"));
            Thread.Sleep(60000);

            HttpGet(new Uri(String.Format("http://{0}.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.OK);

            HttpGet(new Uri(String.Format("https://{0}.{1}", _site, _dnsSuffix)),
                null,
                HttpStatusCode.OK);

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

            // http forward should lead to notifyfull
            RunAndValidate("!Idle: True", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("State: Running", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            EnableAllWorkers();

            // slave should scale back
            // TODO, suwatch: flaky
            // HACK HACK
            // wait for Notify Full to stale
            Console.WriteLine("{0} Sleep 300s", DateTime.Now.ToString("o"));
            Thread.Sleep(300000);
            RunAndValidate("Completed successfully.",
                _geoRegionCmd,
                "Notify {0} {1} {2} {3} {4} {5}",
                _blu2, _free, _sub, _ws, _site, _sf);

            RunAndValidate("Idle: True", _geoRegionCmd, 120, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
        }

        static void DisableAllWorkers()
        {
            foreach (var worker in GetAllWorkers())
            {
                SetWorkerState(worker, enabled: false);
            }

            CheckCapacity(_blu1Cmd, hasCapacity: false);
        }

        static void EnableAllWorkers()
        {
            foreach (var worker in GetAllWorkers())
            {
                SetWorkerState(worker, enabled: true);
            }

            CheckCapacity(_blu1Cmd, hasCapacity: true);
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

        static void SlotCreateFunctionSite()
        {
            // CreateFunctionSite and slot
            RunAndValidate(String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                _geoMasterCmd,
                1,
                "CreateFunctionSite {0} {1} {2} /storageAccount:{3} /storageKey:{4} /appSettings:WEBSITE_LOAD_CERTIFICATES=*,FUNCTIONS_EXTENSION_VERSION=~0.8",
                _sub, _ws, _siteSlot, _storageAccount, _storageKey);
            RunAndValidate(String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                _geoMasterCmd,
                1,
                "CreateWebSiteSlot {0} {1} {2} {3}",
                _sub, _ws, _siteSlot, _slotName);

            // pushing trigger
            RunAndValidate("SyncWebSiteTriggers Response: OK",
                _geoMasterCmd,
                "SyncWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);
            RunAndValidate("CSharpHttpTriggerProduction",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);

            RunAndValidate("SyncWebSiteTriggers Response: OK",
                _geoMasterCmd,
                "SyncWebSiteTriggers {0} {1} {2}({3})",
                _sub, _ws, _siteSlot, _slotName);
            RunAndValidate("CSharpHttpTriggerStaging",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}({3})",
                _sub, _ws, _siteSlot, _slotName);


            // Propagate
            SlotValidateHotBackup();

            // HttpGet Production
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu1HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Production)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu2HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Production)");

            // HttpGet Staging
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu1HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Staging)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu2HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.NotFound);
        }

        static void SlotValidateHotBackup()
        {
            // Check if hot back up Production
            RunAndValidate("StampName: " + _blu1, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _siteSlot);
            RunAndValidate("StampName: " + _blu2, _geoRegionCmd, "ListSiteStamps /siteName:{0}", _siteSlot);
            RunAndValidate("Idle: True", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _siteSlot);
            RunAndValidate(_siteSlot, _blu2Cmd, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("State: Running", _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _siteSlot);
            RunAndValidate("HomeStamp: " + _blu1, _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _siteSlot);
            HttpGet(new Uri(String.Format("http://{0}", _blu1HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK);
            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _siteSlot);
            RunAndValidate("HomeStamp: " + _blu1, _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _siteSlot);

            // Check if hot back up Staging
            RunAndValidate("!StampName: " + _blu1, _geoRegionCmd, "ListSiteStamps /siteName:{0}-{1}", _siteSlot, _slotName.ToLowerInvariant());
            RunAndValidate("!StampName: " + _blu2, _geoRegionCmd, "ListSiteStamps /siteName:{0}-{1}", _siteSlot, _slotName.ToLowerInvariant());

            RunAndValidate("State: Running", _blu1Cmd, "GetWebSite {0} {1} {2}({3})", _sub, _ws, _siteSlot, _slotName.ToLowerInvariant());
            RunAndValidate("HomeStamp: " + _blu1, _blu1Cmd, "GetWebSite {0} {1} {2}({3})", _sub, _ws, _siteSlot, _slotName.ToLowerInvariant());
            RunAndValidate("(404) Not Found", _blu2Cmd, "GetWebSite {0} {1} {2}({3})", _sub, _ws, _siteSlot, _slotName.ToLowerInvariant());

            // ensure trigger propagation
            RunAndValidate("CSharpHttpTriggerProduction",
                _blu2Cmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);
        }

        static void SlotSwapTest_1()
        {
            SwapSiteSlots();

            // Trigger has swap
            RunAndValidate("CSharpHttpTriggerStaging",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);
            RunAndValidate("CSharpHttpTriggerProduction",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}({3})",
                _sub, _ws, _siteSlot, _slotName);

            // ensure trigger propagation
            RunAndValidate("CSharpHttpTriggerStaging",
                _blu2Cmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);

            // HttpGet Production
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu1HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Staging)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu2HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Staging)");

            // HttpGet Staging
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu1HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Production)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu2HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.NotFound);
        }

        static void SlotSwapTest_2()
        {
            SwapSiteSlots();

            // Trigger has swap
            RunAndValidate("CSharpHttpTriggerProduction",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);
            RunAndValidate("CSharpHttpTriggerStaging",
                _geoMasterCmd,
                "GetWebSiteTriggers {0} {1} {2}({3})",
                _sub, _ws, _siteSlot, _slotName);

            // ensure trigger propagation
            RunAndValidate("CSharpHttpTriggerProduction",
                _blu2Cmd,
                "GetWebSiteTriggers {0} {1} {2}",
                _sub, _ws, _siteSlot);

            // HttpGet Production
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu1HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Production)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerProduction?name=foo", _blu2HostName)),
                String.Format("{0}.{1}", _siteSlot, _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Production)");

            // HttpGet Staging
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu1HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.OK,
                expectedContent: _siteSlot + "(Staging)");
            HttpGet(new Uri(String.Format("http://{0}/api/CSharpHttpTriggerStaging?name=foo", _blu2HostName)),
                String.Format("{0}-{1}.{2}", _siteSlot, _slotName.ToLowerInvariant(), _dnsSuffix),
                HttpStatusCode.NotFound);
        }

        static void SwapSiteSlots()
        {
            Console.WriteLine(DateTime.Now.ToString("o"));

            //RequestID = 3dc25123-0e04-4674-932e-994d74f2a642, request created at 2016-09-26T05:37:54.5169457Z
            //Request to swap site slot 'functionslot200' with slot 'Staging' has been submitted.
            //Use the command below to get the status of the request:
            //AntaresCmd.exe GetWebSiteOperation 00e7bb72-7725-4249-8e6b-0d2632b3bfc1 eastuswebspace functionslot200
            //df6eb4ec-3ebd-407c-a534-03b8e54565cb
            var output = Run(_geoMasterCmd, "SwapWebSiteSlots {0} {1} {2} {3}", _sub, _ws, _siteSlot, _slotName);
            var success = false;
            var operationCmd = string.Empty;
            using (var reader = new StringReader(output))
            {
                while (string.IsNullOrEmpty(operationCmd))
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        break;

                    if (success)
                    {
                        if (line.Trim().StartsWith("AntaresCmd.exe GetWebSiteOperation "))
                        {
                            operationCmd = line.Trim();
                            break;
                        }
                        continue;
                    }

                    success = line.Contains(string.Format("Request to swap site slot '{0}' with slot '{1}' has been submitted", _siteSlot, _slotName));
                }
            }

            if (!success || string.IsNullOrEmpty(operationCmd))
            {
                throw new InvalidOperationException("Fail SwapSiteSlots\r\n" + output);
            }

            RunAndValidate("Status: Succeeded",
                _geoMasterCmd,
                operationCmd.Replace("AntaresCmd.exe ", string.Empty));
        }

        static void NotifyFullTest()
        {
            // ensure capacities
            CheckCapacity(_blu1Cmd, hasCapacity: true);
            CheckCapacity(_blu2Cmd, hasCapacity: true);

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

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);

            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.OK);
        }

        static void CheckCapacity(string antaresCmd, bool hasCapacity)
        {
            RunAndValidate("Size: 1536, Available: ",
                antaresCmd,
                "GetDynamicSkuContainerCapacities");
            if (hasCapacity)
            {
                RunAndValidate("!Size: 1536, Available: 0",
                    antaresCmd,
                    "GetDynamicSkuContainerCapacities");
            }
            else
            {
                RunAndValidate("Size: 1536, Available: 0",
                    antaresCmd,
                    "GetDynamicSkuContainerCapacities");
            }
        }

        static void ValidateConfigPropagation()
        {
            // compensate for clock skew
            Console.WriteLine("{0} Sleep 60s", DateTime.Now.ToString("o"));
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


            RunAndValidate("HomeStamp: " + _blu1, _blu1Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            RunAndValidate("HomeStamp: " + _blu1, _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);
        }

        static void ValidateServerFarmPropagation()
        {
            // compensate for clock skew
            Console.WriteLine("{0} Sleep 60s", DateTime.Now.ToString("o"));
            Thread.Sleep(60000);

            var lastModifiedTime1 = GetServerFarmLastModifiedTime(_geoMasterCmd);
            var lastModifiedTime2 = GetServerFarmLastModifiedTime(_blu2Cmd);

            if (lastModifiedTime1 > lastModifiedTime2)
            {
                throw new InvalidOperationException(lastModifiedTime1 + " > " + lastModifiedTime2);
            }

            // UpdateServerFarm propagation
            RunAndValidate(String.Format("Server farm {0} has been updated.", _sf),
                _geoMasterCmd,
                "UpdateServerFarm {0} {1} {2} Dynamic /workerSize:0",
                _sub, _ws, _sf);

            var lastModifiedTime = GetServerFarmLastModifiedTime(_geoMasterCmd);
            if (lastModifiedTime1 > lastModifiedTime)
            {
                throw new InvalidOperationException(lastModifiedTime1 + " > " + lastModifiedTime);
            }
            if (lastModifiedTime2 > lastModifiedTime)
            {
                throw new InvalidOperationException(lastModifiedTime2 + " > " + lastModifiedTime);
            }

            lastModifiedTime1 = lastModifiedTime;
            lastModifiedTime = lastModifiedTime2;

            for (int i = 24; lastModifiedTime == lastModifiedTime2; --i)
            {
                if (i <= 0)
                {
                    throw new InvalidOperationException("Timeout waiting for serverFarm change");
                }

                Thread.Sleep(5000);

                lastModifiedTime = GetServerFarmLastModifiedTime(_blu2Cmd);
            }

            if (lastModifiedTime1 > lastModifiedTime)
            {
                throw new InvalidOperationException(lastModifiedTime1 + " > " + lastModifiedTime);
            }
            if (lastModifiedTime2 > lastModifiedTime)
            {
                throw new InvalidOperationException(lastModifiedTime2 + " > " + lastModifiedTime);
            }
        }

        static DateTime GetServerFarmLastModifiedTime(string cmd)
        {
            Console.WriteLine(DateTime.Now.ToString("o"));
            var output = Run(cmd, "GetServerFarm {0} {1} {2}", _sub, _ws, _sf);

            using (var reader = new StringReader(output))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    // LastModifiedTimeUtc: 8/31/2016 9:23:38 PM
                    var parts = line.Trim().Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && parts[0] == "LastModifiedTimeUtc")
                    {
                        return DateTime.Parse(parts[1].Trim()).ToLocalTime();
                    }
                }
            }

            throw new InvalidOperationException(output);
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
            Console.WriteLine("{0} Sleep 60s", DateTime.Now.ToString("o"));
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
                HttpStatusCode.OK);

            RunAndValidate("State: Stopped", _blu2Cmd, "GetWebSite {0} {1} {2}", _sub, _ws, _site);

            // always serve incoming request to slave stamp
            HttpGet(new Uri(String.Format("http://{0}", _blu2HostName)),
                String.Format("{0}.{1}", _site, _dnsSuffix),
                HttpStatusCode.OK);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), _blu1IPAddress, "!" + _blu2IPAddress);
        }

        static void DeleteFunctionSite()
        {
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                1,
                "DeleteWebSite {0} {1} {2} /deleteEmptyServerFarm /deleteAllSlots",
                _sub, _ws, _siteSlot);
            RunAndValidate(String.Empty,
                _geoMasterCmd,
                1,
                "DeleteWebSite {0} {1} {2} /deleteEmptyServerFarm  /deleteAllSlots",
                _sub, _ws, _site);

            RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _site);
            RunAndValidate("!" + _site, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _site, _blu2Cmd, 240, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate("![0]", _geoRegionCmd, "ListSiteStamps /siteName:{0}", _siteSlot);
            RunAndValidate("!" + _siteSlot, _geoMasterCmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _siteSlot, _blu1Cmd, "ListWebSites {0} {1}", _sub, _ws);
            RunAndValidate("!" + _siteSlot, _blu2Cmd, 240, "ListWebSites {0} {1}", _sub, _ws);

            RunAndValidate(_ws, _blu1Cmd, "ListWebSpaces {0}", _sub);
            RunAndValidate("!" + _ws, _blu2Cmd, "ListWebSpaces {0}", _sub);

            ValidateDnsHostEntry(String.Format("{0}.{1}", _site, _dnsSuffix), "!" + _blu1IPAddress, "!" + _blu2IPAddress);
            ValidateDnsHostEntry(String.Format("{0}.scm.{1}", _site, _dnsSuffix), "!" + _blu1IPAddress, "!" + _blu2IPAddress);
        }

        static bool HasTimerTrigger()
        {
            var request = GetTimerTriggerRequest();
            request.Method = "GET";
            var requestId = Guid.Empty.ToString().Replace("00000000-", Interlocked.Increment(ref _requestId) + "-");
            request.Headers.Add("x-ms-request-id", requestId);

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", HasTimerTrigger ");
            Console.Write("x-ms-request-id: " + requestId + " ");
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
            var requestId = Guid.Empty.ToString().Replace("00000000-", Interlocked.Increment(ref _requestId) + "-");
            request.Headers.Add("x-ms-request-id", requestId);

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", DeleteTimerTrigger ");
            Console.Write("x-ms-request-id: " + requestId + " ");
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

        static void RetryHelper(Action action)
        {
            int max = 5;
            while (true)
            {
                try
                {
                    action();
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    if (max-- < 0)
                        throw;
                }

                Thread.Sleep(5000);
            }
        }

        static void AddTimerTrigger()
        {
            const string TimerTriggerFile = @"c:\temp\SampleTimerTrigger_function.json";

            var request = GetTimerTriggerRequest();
            request.Method = "PUT";
            request.Headers.Add("If-Match", "*");
            request.ContentType = "application/json";
            var requestId = Guid.Empty.ToString().Replace("00000000-", Interlocked.Increment(ref _requestId) + "-");
            request.Headers.Add("x-ms-request-id", requestId);

            Console.WriteLine();
            Console.Write(DateTime.Now.ToString("o") + ", AddTimerTrigger ");
            Console.Write("x-ms-request-id: " + requestId + " ");
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
            var timerTriggerUrl = String.Format("https://{0}/vfs/site/wwwroot/SampleTimerTrigger/function.json", _blu1HostName);
            var request = (HttpWebRequest)WebRequest.Create(timerTriggerUrl);
            request.Credentials = new NetworkCredential("auxtm230", "iis6!dfu");
            request.Host = String.Format("{0}.scm.{1}", _site, _dnsSuffix);
            request.UserAgent = "CrossStampFunctional/0.0.0.0";

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
                else if (output.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
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
                var requestId = Guid.Empty.ToString().Replace("00000000-", Interlocked.Increment(ref _requestId) + "-");
                Console.WriteLine(DateTime.Now.ToString("o"));
                Console.WriteLine("HttpGet: {0}", uri);
                Console.WriteLine("Host: {0}", host);
                Console.WriteLine("x-ms-request-id: {0}", requestId);
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    if (host != null)
                    {
                        client.DefaultRequestHeaders.Host = host;
                    }

                    client.DefaultRequestHeaders.Add("x-ms-request-id", requestId);

                    if (userName != null && password != null)
                    {
                        var byteArray = Encoding.ASCII.GetBytes(String.Format("{0}:{1}", userName, password));
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }

                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("CrossStampFunctional", "1.0.0.0")));

                    try
                    {
                        var now = DateTime.UtcNow;
                        using (var response = client.GetAsync(uri).Result)
                        {
                            Console.WriteLine("Latency: {0} ms", (int)(DateTime.UtcNow - now).TotalMilliseconds);
                            if (response.StatusCode == expected && IsResponseContentMatch(expectedContent, response))
                            {
                                Console.WriteLine("HttpStatus: {0} == {1}", response.StatusCode, expected);
                                Console.WriteLine("Passed.");
                                Console.WriteLine();
                                return;
                            }

                            if (response.StatusCode != expected)
                            {
                                Console.WriteLine("HttpStatus: {0} != {1}", response.StatusCode, expected);
                            }
                            else
                            {
                                Console.WriteLine("Content Not Match: {0}", expectedContent);
                            }

                            IEnumerable<string> poweredBys;
                            if (response.Headers.TryGetValues("X-Powered-By", out poweredBys))
                            {
                                Console.WriteLine("X-Powered-By: {0}", string.Join("; ", poweredBys));
                            }
                            else
                            {
                                Console.WriteLine("No X-Powered-By header!");
                            }

                            Console.WriteLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception: {0}", ex);
                        Console.WriteLine();
                    }
                }

                Thread.Sleep(5000);
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