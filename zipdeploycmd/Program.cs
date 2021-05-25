using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;


namespace zipdeploycmd
{

    public class PublishConfig
    {
        public string Name { get; set; }
        public string ScmHostName { get; set; }
        public string UserName { get; set; }
        public string PasswordEnc { get; set; }
        public DateTime DT { get; set; }
    }



    class Program
    {

        static byte[] entropy = new byte[] { 67, 89, 34, 25, 123, 2, 5, 3, 1 };


        static int Main(string[] args)
        {
            try
            {
                // Default proxy with default credential (required in some corporate environments)
                // Same result uting app.config -> system.net -> defaultProxy.useDefaultCredentials=true
                WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

                if (args.Length == 0)
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("app.exe  IMPORT   pubfilename.xml");
                    Console.WriteLine("app.exe  PUBLISH  pubconfig.json  wwwroot.zip");
                    return 1; // error
                }

                string mode = args[0].ToUpper();

                switch (mode)
                {
                    case "IMPORT": ImportPuglishProfile(args[1]); break;
                    case "PUBLISH": ExecuteZipDeployPublish(args[1], args[2]); break;
                    default: throw new ApplicationException($"Unknown mode [{mode}]");
                }

                return 0; // OK
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return 1; // error
            }

        }


        private static void ImportPuglishProfile(string publishFilenName)
        {
            string name = Path.GetFileNameWithoutExtension(publishFilenName);

            var xdoc = new XmlDocument();
            xdoc.Load(publishFilenName);
            var xnode = (XmlElement)xdoc.SelectSingleNode("/publishData/publishProfile[@publishMethod='ZipDeploy']");
            string userName = xnode.GetAttribute("userName");
            string password = xnode.GetAttribute("userPWD");
            string scmHostname = xnode.GetAttribute("publishUrl").Split(':')[0];

            string passwordEnc = Convert.ToBase64String(
                                    ProtectedData.Protect(
                                        Encoding.UTF8.GetBytes(password),
                                        entropy,
                                        DataProtectionScope.CurrentUser));

            var pubData = new PublishConfig()
            {
                Name = name,
                ScmHostName = scmHostname,
                UserName = userName,
                PasswordEnc = passwordEnc,
                DT = DateTime.UtcNow
            };

            string json = JsonConvert.SerializeObject(pubData, Newtonsoft.Json.Formatting.Indented);

            File.WriteAllText($"{name}.pubconfig.json", json);
        }


        private static void ExecuteZipDeployPublish(string pubconfigFilename, string zipFileName)
        {
            Console.WriteLine("Read config");
            var pubConfig = JsonConvert.DeserializeObject<PublishConfig>(File.ReadAllText(pubconfigFilename));
            string baseURL = $"https://{pubConfig.ScmHostName}/api";
            Console.WriteLine($"URL: {baseURL}");

            // prepare httpclient with user+pass
            var httpClient = new HttpClient();

            string password = Encoding.UTF8.GetString(
                                ProtectedData.Unprotect(
                                    Convert.FromBase64String(pubConfig.PasswordEnc),
                                    entropy,
                                    DataProtectionScope.CurrentUser));

            var authHeader = new AuthenticationHeaderValue(
                                    "Basic",
                                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{pubConfig.UserName}:{password}")));

            httpClient.DefaultRequestHeaders.Authorization = authHeader;

            // read zip content
            var content = new StreamContent(new MemoryStream(File.ReadAllBytes(zipFileName)));

            // this call used only to check if the authentication works as expected
            Console.WriteLine("Verify if site is up&running");
            string scmInfo = httpClient.GetStringAsync($"{baseURL}/deployments").Result;

            // push zip file
            Console.WriteLine("Publish");
            var resp = httpClient.PostAsync($"{baseURL}/zipdeploy", content).Result;
            resp.EnsureSuccessStatusCode();
            var responseString = resp.Content.ReadAsStringAsync().Result;
            Console.WriteLine("Done");
        }

    }

}
