using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Insidash.DAL.Context;

namespace Insidash.TallyApi.Filters
{
    public class SyncTokenAuthFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext == null || actionContext.Request == null)
            {
                base.OnActionExecuting(actionContext);
                return;
            }

            if (!actionContext.Request.Headers.Contains("X-Sync-Token"))
            {
                actionContext.Response = actionContext.Request.CreateResponse(
                    HttpStatusCode.Unauthorized,
                    "Missing X-Sync-Token header");
                return;
            }

            string token = actionContext.Request.Headers.GetValues("X-Sync-Token").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                actionContext.Response = actionContext.Request.CreateResponse(
                    HttpStatusCode.Unauthorized,
                    "Missing X-Sync-Token header");
                return;
            }

            using (var db = new InsidashTallyContext())
            {
                bool isValid = db.TallyCompanyConfigs.Any(c => c.SyncToken == token && c.IsActive);
                if (!isValid)
                {
                    actionContext.Response = actionContext.Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        "Invalid or inactive sync token");
                    return;
                }
            }

            base.OnActionExecuting(actionContext);
        }
    }
}