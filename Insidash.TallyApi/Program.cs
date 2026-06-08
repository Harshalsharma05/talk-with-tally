using System;
using System.Web.Http;
using System.Web.Http.SelfHost;

namespace Insidash.TallyApi
{
    class Program
    {
        static void Main(string[] args)
        {
            const string baseAddress = "http://localhost:8081";

            // Configure the local address and port for the API to listen to
            var config = new HttpSelfHostConfiguration(baseAddress);
            config.MaxReceivedMessageSize = 2147483647;
            config.MaxBufferSize = 2147483647;

            // Register all Web API configuration (routes, CORS, JSON formatting)
            WebApiConfig.Register(config);

            // Initialize and boot up the internal HTTP Web Server
            using (HttpSelfHostServer server = new HttpSelfHostServer(config))
            {
                server.OpenAsync().Wait();
                Console.WriteLine("\n==================================================");
                Console.WriteLine("  INSIDASH TALLY WEB API LIVE ENGINE (SELF-HOSTED) ");
                Console.WriteLine("  Listening continuously at: " + baseAddress + " ");
                Console.WriteLine("==================================================\n");
                Console.WriteLine("Press [Enter] at any time to shut down the server.");
                Console.ReadLine();
            }
        }
    }
}