using System;
using System.Linq;
using System.Collections.Generic;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public class AIChatLogRepository : IAIChatLogRepository
    {
        public void InsertLog(AIChatLog log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));

            using (var ctx = new InsidashTallyContext())
            {
                // If LogID is zero, assume DB will generate identity (if configured)
                ctx.AIChatLogs.Add(log);
                ctx.SaveChanges();
            }
        }

        public IEnumerable<AIChatLog> GetByCompany(int companyId, int page = 1, int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            using (var ctx = new InsidashTallyContext())
            {
                return ctx.AIChatLogs
                    .Where(l => l.CompanyID == companyId)
                    .OrderByDescending(l => l.CreatedDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
        }
    }
}
