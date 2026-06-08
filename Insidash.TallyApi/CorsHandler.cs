using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Insidash.TallyApi
{
    public class CorsHandler : DelegatingHandler
    {
        private const string Origin = "Origin";
        private const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";
        private const string AccessControlAllowHeaders = "Access-Control-Allow-Headers";
        private const string AccessControlAllowMethods = "Access-Control-Allow-Methods";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool isCorsRequest = request.Headers.Contains(Origin);
            bool isPreflightRequest = request.Method == HttpMethod.Options;

            if (isCorsRequest)
            {
                if (isPreflightRequest)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    
                    // Echo the requested origin back
                    string reqOrigin = request.Headers.GetValues(Origin).FirstOrDefault();
                    response.Headers.Add(AccessControlAllowOrigin, reqOrigin);
                    response.Headers.Add(AccessControlAllowHeaders, "Content-Type, X-Sync-Token, X-Company-ID");
                    response.Headers.Add(AccessControlAllowMethods, "GET, POST, OPTIONS, PUT, DELETE");
                    
                    var tcs = new TaskCompletionSource<HttpResponseMessage>();
                    tcs.SetResult(response);
                    return tcs.Task;
                }
                
                return base.SendAsync(request, cancellationToken).ContinueWith(t =>
                {
                    var resp = t.Result;
                    string reqOrigin = request.Headers.GetValues(Origin).FirstOrDefault();
                    resp.Headers.Add(AccessControlAllowOrigin, reqOrigin);
                    return resp;
                }, cancellationToken);
            }
            
            return base.SendAsync(request, cancellationToken);
        }
    }
}
